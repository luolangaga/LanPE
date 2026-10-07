# tools/ — 打包工具链

`scripts/package-tools.ps1` 会把本目录打包为 `release/assets/lanpe-tools.zip`，供打包器在组装/出 ISO 时使用。
二进制体积较大，默认 **不入库**（见 `.gitignore` 的 `tools/bin/`）。请按下表放入对应工具。

| 子目录 | 工具 | 用途 | 建议来源 |
|---|---|---|---|
| `xorriso/` | `xorriso.exe` | 生成 ISO（grub-mkrescue 的底层） | 官方 / MSYS2 包 |
| `grub2/` | `grub-mkrescue`、`x86_64-efi/`、`i386-pc/`、`BOOTX64.EFI`、`isohdpfx.bin` | 生成 hybrid 引导 ISO | GNU GRUB 官方 release / 发行版包 |
| `wimlib/` | `wimlib-imagex.exe` | 处理与压缩 wim | wimlib 官方 release |
| `wimboot/` | `wimboot`、`wimboot.efi` | 备选引导（iPXE） | ipxe 官方 wimboot release |
| `bin/` | `7za.exe` | 解压 7z 软件包 | 7-Zip 官方 |

> `bcdedit` / `dism` 使用系统自带，不在此打包。

## 目录约定

`Toolchain.Locate()` 会在本目录（递归）中按文件名查找各工具，因此子目录名可自由组织，只要文件名正确：

- `xorriso.exe`（或 `xorriso`）
- `grub-mkrescue` / `grub-mkrescue.exe` / `grub-mkrescue.bat` / `grub-mkrescue.sh`
- `wimlib-imagex.exe`（或 `wimlib-imagex`）
- `7za.exe`（或 `7z.exe`）

## 版本记录

| 工具 | 版本 | 来源 URL | 日期 |
|---|---|---|---|
| xorriso | （待填） | | |
| GNU GRUB | （待填） | | |
| wimlib | （待填） | | |
| wimboot | （待填） | | |
| 7-Zip | （待填） | | |
