# PE 构建（BUILD-PE）

用微软官方 **Windows ADK + WinPE 加载项** 从零构建 Win11 PE 与 Win10 PE，产出打包器所需的
`win11pe-media.zip` / `win10pe-media.zip`。

## 1. 安装 ADK

- **Windows 11 ADK**（含 WinPE add-on）：
  https://learn.microsoft.com/windows-hardware/get-started/adk-install
  下载 `adksetup.exe` 与 `adkwinpesetup.exe`，两者都装，勾选 **Deployment Tools** 与 **Windows PE**。

安装后 `copype.cmd` 位于：
```
C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\copype.cmd
```

## 2. 生成 Win11 PE

```powershell
# 管理员 PowerShell
.\scripts\build-pe-win11.ps1
# 产物：release\assets\win11pe-media.zip
```

脚本流程：

1. `copype amd64 <work>\pe11` 生成基础媒体树
2. `Dism /Mount-Image` 挂载 `sources\boot.wim`（index 1）
3. 注入可选组件：WMI / NetFX / Scripting / PowerShell / DismCmdlets / StorageWMI / EnhancedStorage
4. 写入 `startnet.cmd` 启动器（探测 `X:\LanPE\launcher\PELauncher.exe`）
5. `Dism /Unmount-Image /Commit`
6. `wimlib-imagex optimize --compress=LZX:100` 最大化压缩
7. `Compress-Archive` 打包媒体树

## 3. 生成 Win10 PE

ADK 版本冲突说明：**同一台机器通常只能安装一个版本的 ADK**（Win11 与 Win10 的 ADK 共存会互相覆盖
`copype` / `dandiset` 等）。因此有两条路：

### 方式 A：在装有 Win10 ADK 的机器/VM 上
```powershell
.\scripts\build-pe-win10.ps1 -UseAdk
```

### 方式 B（推荐）：在本机从官方 Win10 ISO 抽取 + 定制
无需旧版 ADK。用官方 Win10 ISO（`sources/boot.wim` 的 index 2 即 WinPE）：
```powershell
.\scripts\build-pe-win10.ps1 -Win10Iso D:\ISO\Win10_22H2_Chinese_x64.iso
```

脚本会挂载 ISO、抽出 `boot.wim` 与引导文件（`bootmgr` / `boot\` / `EFI\`），再挂载定制并打包。

## 4. PE 内的“旁挂软件”机制

LanPE 的一个关键设计：**内置软件不打进 wim，而是在打包时解到介质目录**，PE 启动后由启动器扫描
并生成快捷方式。这样用户才能“按次勾选软件，而无需重新构建 PE”。

打包器会把选中的软件解到：
```
<介质根>\pe11\Apps\<id>\      （WinPE 组件）
```
PE 运行时的介质映射盘符为 `X:`（WinPE 默认），故启动器扫描 `X:\Apps\*`。

若需要更完整的开始菜单/桌面快捷方式体验，可在 PE 内集成第三方外壳（如 PECMD / WinXShell），
由 `startnet.cmd` 调用它们并传入 `Apps` 目录。相关启动器（`PELauncher.exe`）作为内容资产，
放在 `release/assets/` 中随 PE 媒体一并分发。

## 5. 校验产物

```powershell
Get-FileHash .\release\assets\win11pe-media.zip -Algorithm SHA256
```
生成的哈希会在 `gen-manifest.ps1` 中自动写入 `manifest.json`。
