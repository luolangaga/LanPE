# LanPE

**LanPE** 是一个组件化的多启动介质打包器。它从 GitHub Release 拉取组件，按需勾选组装成一份可引导介质，并输出为 **ISO / 写入 U 盘 / 安装到本地磁盘** 三种形态。

内置组件：

| 组件 | 类型 | 引导方式 |
|---|---|---|
| Windows 11 PE | `winpe` | 原生 BCD 链式引导（UEFI + BIOS） |
| Windows 10 PE | `winpe` | 原生 BCD 链式引导（UEFI + BIOS） |
| SystemRescue（Linux 救援） | `linux` | GRUB `loopback` 引导 |

每个组件内置的软件（DiskGenius、BOOTICE 等）以**旁挂目录**方式随介质释放，可独立勾选，无需重打 `boot.wim`。

## 目录结构

```
src/        C# 源码（WinForms 打包器 + 类库 + 单测）
tools/      打包所需工具链（xorriso / grub2 / wimlib / wimboot / 7za）
release/    发布清单 manifest.json 与待上传资产
scripts/    内容生产与发布脚本（PowerShell）
docs/       架构、清单格式、PE 构建、使用说明
```

## 技术栈

- **UI**：C# WinForms，.NET Framework 4.8（`requireAdministrator`）
- **打包引擎**：外部工具组 —— `grub-mkrescue`/`xorriso` 生成 hybrid ISO；`wimlib` 处理 wim；Windows API 直写物理盘
- **发布**：GitHub Releases + GitHub Actions

文档入口：

- [引导架构](docs/BOOT-ARCHITECTURE.md)
- [清单格式](docs/MANIFEST-SCHEMA.md)
- [PE 构建](docs/BUILD-PE.md)
- [使用说明](docs/USAGE.md)

## 快速开始

（构建与运行说明见 [docs/USAGE.md](docs/USAGE.md)）
