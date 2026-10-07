<#
.SYNOPSIS
  用 Windows ADK + WinPE 加载项从零构建 Win11 PE 媒体树，并打包为 win11pe-media.zip。

.DESCRIPTION
  依赖已安装的 "Windows ADK for Windows 11" 与 "Windows PE add-on"。
  产物：release/assets/win11pe-media.zip（含 bootmgr/BCD/boot.sdi/sources/boot.wim 等完整媒体树）。

.NOTES
  需管理员权限（DISM 挂载镜像）。
#>
[CmdletBinding()]
param(
    [string] $WorkRoot = "$env:USERPROFILE\LanPE-build",
    [string] $OutDir   = "$PSScriptRoot\..\release\assets",
    [string] $PeDir    = "pe11",
    [switch] $SkipRemount
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Win11PE] $m" -ForegroundColor Cyan }
function Fail($m){ Write-Host "[Win11PE] $m" -ForegroundColor Red; exit 1 }

# --- 定位 ADK ---
$adkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
if (-not $adkRoot) { $adkRoot = 'C:\Program Files (x86)\Windows Kits\10\' }
$copype = Join-Path $adkRoot 'Assessment and Deployment Kit\Windows Preinstallation Environment\copype.cmd'
if (-not (Test-Path $copype)) { Fail "未找到 copype.cmd，请先安装 Windows ADK 的 WinPE 加载项。路径：$copype" }

$dism = "$env:SystemRoot\System32\Dism.exe"

New-Item -ItemType Directory -Force -Path $WorkRoot, $OutDir | Out-Null
$peRoot  = Join-Path $WorkRoot $PeDir
$mediaDir= Join-Path $peRoot 'media'
$mountDir= Join-Path $peRoot 'mount'

# --- 1) 构建基础媒体树 ---
if (-not $SkipRemount) {
    if (Test-Path $peRoot) { Remove-Item $peRoot -Recurse -Force }
    Info "copype amd64 -> $peRoot"
    & cmd /c "`"$copype`" amd64 `"$peRoot`""
    if ($LASTEXITCODE -ne 0) { Fail "copype 失败（$LASTEXITCODE）" }
}

$bootWim = Join-Path $mediaDir 'sources\boot.wim'
if (-not (Test-Path $bootWim)) { Fail "未找到 boot.wim：$bootWim" }

# --- 2) 挂载并注入可选组件 ---
Info "挂载 boot.wim index:1"
& $dism /Mount-Image /ImageFile:"$bootWim" /Index:1 /MountDir:"$mountDir"

$ocDir = Join-Path $adkRoot 'Assessment and Deployment Kit\Windows Preinstallation Environment\amd64\WinPE_OCs'
if (Test-Path $ocDir) {
    foreach ($oc in @('WinPE-WMI','WinPE-NetFX','WinPE-Scripting','WinPE-PowerShell','WinPE-DismCmdlets','WinPE-StorageWMI','WinPE-EnhancedStorage')) {
        $cab = Join-Path $ocDir "$oc.cab"
        if (Test-Path $cab) {
            Info "注入组件：$oc"
            & $dism /Add-Package /Image:"$mountDir" /PackagePath:"$cab" | Out-Null
            $lp = Join-Path $ocDir "en-us\$oc-en-us.cab"
            if (Test-Path $lp) { & $dism /Add-Package /Image:"$mountDir" /PackagePath:"$lp" | Out-Null }
        }
    }
} else {
    Write-Warning "未找到 WinPE_OCs 目录，跳过可选组件：$ocDir"
}

# --- 3) 放置启动器（扫描 X:\Apps 生成快捷方式，供旁挂软件使用） ---
$startnet = Join-Path $mountDir 'Windows\System32\startnet.cmd'
$launcher = @'
@echo off
wpeinit
echo (lanpe) 正在启动维护环境...
if exist X:\LanPE\launcher\PELauncher.exe (
    X:\LanPE\launcher\PELauncher.exe
) else (
    cmd.exe
)
'@
Set-Content -Path $startnet -Value $launcher -Encoding ASCII

# --- 4) 提交 ---
Info "提交并卸载"
& $dism /Unmount-Image /MountDir:"$mountDir" /Commit
if ($LASTEXITCODE -ne 0) { Fail "DISM 提交失败（$LASTEXITCODE）" }

# --- 5) 压缩 wim（若工具链含 wimlib） ---
$wimlib = Get-Command wimlib-imagex.exe -ErrorAction SilentlyContinue
if ($wimlib) {
    Info "用 wimlib 最大化压缩 boot.wim"
    & $wimlib.Source optimize "$bootWim" --compress=LZX:100
}

# --- 6) 打包媒体树 ---
$zip = Join-Path $OutDir 'win11pe-media.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Info "打包 -> $zip"
Compress-Archive -Path (Join-Path $mediaDir '*') -DestinationPath $zip -CompressionLevel Optimal

Info "完成：$zip"
