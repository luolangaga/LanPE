# 使用说明（USAGE）

## 一、技术栈

- **UI**：[MewUI](https://github.com/aprillz/MewUI) — 纯 C# Markup（无 XAML），现代渲染
- **运行时**：.NET 10，发布为 **Native AOT** 单文件（约 9.5 MB，无需安装 .NET 运行时）
- **构建后端**：Win32 + Direct2D

## 二、用户：使用打包器

1. 运行 `LanPE.Packer.exe`（**写 U 盘与本地安装需管理员权限**）。
2. 顶部确认来源（默认 `luolangaga/LanPE` 最新 Release），点 **拉取清单**。
3. 左侧勾选组件；右侧查看详情：
   - **启动选项**（如 RAMDisk 模式）
   - **内置软件**：逐个勾选随介质一起释放的工具
4. 底部动作：
   - **下载所选** —— 仅下载并校验（缓存到 `%LocalAppData%\LanPE\cache`）
   - **生成 ISO** —— 下载 → 组装 → 生成 BIOS+UEFI 双引导 hybrid ISO
   - **写入 U 盘** —— DD 直写（二次确认，输入 `ERASE`）
   - **安装到本地** —— 数据分区 + ESP 注册 UEFI 启动项，提供卸载
5. **打开输出目录** 查看产物；**设置** 切换清单来源与 Token。

## 三、维护者：生产资产

```powershell
# 1) 从 FirPE 提取便携工具（硬盘/硬件/系统/文件/备份/其他 + GRUB/DOS 引导工具）
.\scripts\extract-firpe-tools.ps1

# 2) 生产三套系统载荷（Win11 PE / Win10 PE / SystemRescue + wimboot）
.\scripts\build-payloads.ps1

# 3) 生成清单（计算所有 sha256/size）
.\scripts\gen-manifest.ps1 -ReleaseTag v1.0.0
```

`release/assets/` 下大文件被 `.gitignore` 忽略，不入库；由 CI 或 `gh` 上传到 Release。

## 四、发布

```powershell
git tag v1.0.0
git push origin v1.0.0
```

推 tag 触发 `release.yml`：AOT 发布 → 打包 exe → 生成清单 → 创建 Release。

本地也可用 GitHub CLI：

```powershell
gh release create v1.0.0 release/manifest.json release/assets/LanPE.Packer.zip
```

## 五、开发

```powershell
dotnet build src\LanPE.sln -c Release
dotnet test  src\LanPE.Tests\LanPE.Tests.csproj -c Release

# Native AOT 发布（单文件）
dotnet publish src\LanPE.UI\LanPE.UI.csproj -c Release -r win-x64 -p:MewUIBackend=Direct2D
```

### 工程结构

| 工程 | 说明 |
|---|---|
| `LanPE.UI` | MewUI 界面（net10，Native AOT） |
| `LanPE.Core` | 清单模型/解析（STJ 源生成）、下载、哈希、归档、进程封装、设置 |
| `LanPE.Build` | 组装、GRUB 配置、ISO 构建、工具链、流程编排 |
| `LanPE.Disk` | 物理盘枚举（原生 API）、DD 写盘、本地安装（bcdedit） |
| `LanPE.Tests` | 单元测试 |

### AOT 注意事项

- JSON 一律走 `System.Text.Json` **源生成**（`JsonSerializerIsReflectionEnabledByDefault=false`），
  禁用反射序列化。
- 物理盘枚举用 **SetupAPI/DeviceIoControl P/Invoke**，不用 `System.Management`——后者在
  NativeAOT 下依赖 COM 互操作，运行时会抛异常。
- MewUI 的 DevTools 依赖反射，AOT 下不可用，已通过 `MewUIDevTools=false` 裁剪。

## 六、引导验证（务必实测）

```powershell
# UEFI（需 OVMF 固件）
qemu-system-x86_64 -bios OVMF.fd -cdrom LanPE.iso -m 4096

# Legacy BIOS
qemu-system-x86_64 -cdrom LanPE.iso -m 4096
```

或用 Hyper-V：Gen2 验证 UEFI、Gen1 验证 BIOS，逐项确认三个菜单项都能进入系统。

## 七、常见问题

- **拉清单 403/限流** —— 设置里填 GitHub Token。
- **ISO 生成失败** —— 确认 `tools/` 含 `grub-mkrescue` 或 `xorriso`。
- **写盘失败** —— 必须以管理员运行；目标盘不能是系统盘或固定盘。
- **本地安装后看不到启动项** —— 确认 ESP 盘符正确，`bcdedit /enum firmware` 中应有该条目。
