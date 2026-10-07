# 引导架构（BOOT-ARCHITECTURE）

## 1. 总原则

LanPE 只维护**一份中间产物**：BIOS + UEFI 双引导、可 DD 直写的 **hybrid ISO**。
三种输出（存 ISO / 写 U 盘 / 装本地盘）都从它派生。

| 输出 | 实现 |
|---|---|
| 保存 ISO | 直接给文件 |
| 写入 U 盘 | raw（DD）写入 `\\.\PhysicalDriveN`，天然双固件可引导 |
| 安装到本地盘 | 解包到数据分区 + GRUB EFI 写入 ESP + `bcdedit` 注册启动项 |

## 2. Windows PE 为什么用 wimboot

**关键约束**：直接链式引导多个 Windows `bootmgr` 是不可行的 —— `bootmgr` 会到卷根查找
固定的 `\boot\bcd` 与 `\sources\boot.wim`。当两个 PE 共存于同一分区时，它们会争抢同一组
根路径，必然冲突。

标准解法（FirPE / Ventoy 同款）是 **wimboot**：PE 以裸 WIM 分发，引导时由 wimboot 动态构造
ramdisk，按指定路径加载各自的 wim。这正是本项目的做法。

对比：

| 方案 | 多 PE 共存 | 说明 |
|---|---|---|
| 原生 BCD chainload | ❌ | bootmgr 路径固定到卷根，多 PE 冲突 |
| **wimboot** | ✅ | 动态构造 ramdisk，按路径加载指定 wim |
| 各自独立分区 | ✅ | 需要多分区，介质管理复杂 |

## 3. 介质布局（staging）

```
/boot/
  grub/grub.cfg              主菜单
  wimboot                   wimboot 引导器
  bcd                       Windows BCD（共享）
  boot.sdi                  RAMDisk 映像（共享）
  bootmgr                   BIOS 引导管理器
  bootmgr.efi               UEFI 引导管理器
/pe/win11pe/win11pe.wim     Win11 PE 主体
/pe/win10pe/win10pe.wim     Win10 PE 主体
/iso/systemrescue.iso       Linux 救援 ISO
/LanPE/marker               search --file 定位介质根
/LanPE/Apps/<peId>/<swId>/  旁挂软件（PE 内启动器扫描）
```

> 文件名**全小写**：GRUB 在 ISO9660/FAT 上大小写敏感，必须与落盘文件名严格一致。

## 4. 菜单逻辑

### Windows PE（wimboot）

```text
menuentry "Windows 11 PE" {
    search --no-floppy --set=root --file /LanPE/marker
    if [ "$grub_platform" = "efi" ]; then
        linuxefi  /boot/wimboot index=2
        initrdefi newc:bcd:(/boot/bcd) newc:boot.sdi:(/boot/boot.sdi) \
                  newc:bootmgfw.efi:(/boot/bootmgr.efi) newc:boot.wim:(/pe/win11pe/win11pe.wim)
    else
        linux16  /boot/wimboot index=2
        initrd16 newc:bcd:(/boot/bcd) newc:boot.sdi:(/boot/boot.sdi) \
                 newc:bootmgr:(/boot/bootmgr) newc:boot.wim:(/pe/win11pe/win11pe.wim)
    fi
    boot
}
```

`index=2` 是 WIM 内部镜像索引（FirPE 与 Win10 ISO 的 WinPE 都在索引 2）。

### Linux（loopback）

```text
menuentry "SystemRescue" {
    search --no-floppy --set=root --file /LanPE/marker
    loopback loop /iso/systemrescue.iso
    linux  (loop)/sysresccd/boot/x86_64/vmlinuz img_loop=/iso/systemrescue.iso ...
    initrd (loop)/sysresccd/boot/x86_64/sysresccd.img
}
```

## 5. 出 ISO

优先 `grub-mkrescue`（自动产出 hybrid 镜像）；缺失时回退直调 `xorriso`：

```
xorriso -as mkisofs -iso-level 3 -full-iso9660-filenames -volid LanPE \
  -eltorito-alt-boot -e EFI/BOOT/BOOTX64.EFI -no-emul-boot -isohybrid-gpt-basdat \
  -b boot/grub/i386-pc/eltorito.img -no-emul-boot -boot-load-size 4 -boot-info-table \
  -isohybrid-mbr <isohdpfx.bin> -o LanPE.iso <staging>
```

## 6. 写 U 盘与本地安装

- **写 U 盘**：`CreateFile("\\.\PhysicalDriveN")` → 分块 `WriteFile`（4 MiB，尾部补齐到 512B 扇区对齐）
  → `IOCTL_DISK_UPDATE_PROPERTIES`。安全护栏：拒绝系统盘、要求输入 `ERASE`、二次确认。
- **本地安装**：数据解到 `<分区>\LanPE`；GRUB EFI 写到 `\EFI\LanPE\`；
  `bcdedit` 创建 `{guid}` 并加入 `{fwbootmgr}` displayorder。提供卸载。

Legacy 本地盘多启动需改 MBR，风险高，v1 不自动做。

## 7. 旁挂软件机制

内置软件**不进 wim**，打包时解到 `/LanPE/Apps/<peId>/<swId>/`。
PE 启动后由启动器扫描该目录生成快捷方式 —— 这样软件才能按次勾选，无需重打 wim。

## 8. 体积与限制

- FAT32 单文件 4 GB 限制：`win11pe.wim` 752 MB、`win10pe.wim` 611 MB、`systemrescue.iso` 885 MB，
  均远低于限制。
- GitHub Release 单资产 ≤ 2 GB：当前最大资产 885 MB，安全。
