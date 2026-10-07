<#
.SYNOPSIS
  构建 Win10 PE 媒体树并打包为 win10pe-media.zip。

.DESCRIPTION
  两种来源任选：
   A) ADK for Windows 10（v2004 / 1809）+ WinPE 加载项 —— 与本仓库 build-pe-win11.ps1 流程一致。
   B) 从官方 Windows 10 ISO 抽取 boot.wim（index 2 = WinPE）再定制 —— 无需旧版 ADK。

  由于同一台机器通常只能装一个版本的 ADK，推荐用方式 B 在装有新版 ADK 的机器上产出 Win10 PE。

.NOTES
  方式 B 需要提供 Win10 ISO 路径（-Win10Iso）。需管理员权限。
#>
[CmdletBinding(DefaultParameterSetName = 'FromIso')]
param(
    [Parameter(ParameterSetName = 'FromIso', Mandatory = $true)]
    [string] $Win10Iso,

    [Parameter(ParameterSetName = 'FromAdk')]
    [switch] $UseAdk,

    [string] $WorkRoot = "$env:USERPROFILE\LanPE-build",
    [string] $OutDir   = "$PSScriptRoot\..\release\assets",
    [string] $PeDir    = "pe10"
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Win10PE] $m" -ForegroundColor Cyan }
function Fail($m){ Write-Host "[Win10PE] $m" -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force -Path $WorkRoot, $OutDir | Out-Null
$peRoot  = Join-Path $WorkRoot $PeDir
$mediaDir= Join-Path $peRoot 'media'

if (Test-Path $peRoot) { Remove-Item $peRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $mediaDir | Out-Null

if ($UseAdk) {
    # --- 方式 A：ADK 构建 ---
    $adkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
    if (-not $adkRoot) { $adkRoot = 'C:\Program Files (x86)\Windows Kits\10\' }
    $copype = Join-Path $adkRoot 'Assessment and Deployment Kit\Windows Preinstallation Environment\copype.cmd'
    if (-not (Test-Path $copype)) { Fail "未找到 Win10 版 copype.cmd：$copype" }
    Info "copype amd64 -> $peRoot"
    & cmd /c "`"$copype`" amd64 `"$peRoot`""
    if ($LASTEXITCODE -ne 0) { Fail "copype 失败（$LASTEXITCODE）" }
}
else {
    # --- 方式 B：从 Win10 ISO 抽取 ---
    if (-not (Test-Path $Win10Iso)) { Fail "Win10 ISO 不存在：$Win10Iso" }
    Info "挂载 Win10 ISO：$Win10Iso"
    $mount = (Mount-DiskImage -ImagePath (Resolve-Path $Win10Iso) -PassThru | Get-Volume).DriveLetter + ':\'
    try {
        Info "复制 sources/boot.wim 并展开媒体骨架"
        $srcBoot = Join-Path $mount 'sources\boot.wim'
        if (-not (Test-Path $srcBoot)) { Fail "ISO 中未找到 sources\boot.wim" }

        New-Item -ItemType Directory -Force -Path (Join-Path $mediaDir 'sources') | Out-Null
        Copy-Item $srcBoot (Join-Path $mediaDir 'sources\boot.wim')

        # 复制引导文件（bootmgr / boot 目录 / EFI 引导）
        foreach ($f in @('bootmgr','bootmgr.efi')) {
            $p = Join-Path $mount $f
            if (Test-Path $p) { Copy-Item $p $mediaDir }
        }
        foreach ($d in @('boot','EFI')) {
            $p = Join-Path $mount $d
            if (Test-Path $p) { Copy-Item $p $mediaDir -Recurse -Force }
        }
    }
    finally {
        Dismount-DiskImage -ImagePath (Resolve-Path $Win10Iso) | Out-Null
    }
}

# --- 定制 boot.wim：注入脚本组件（可选） ---
$dism = "$env:SystemRoot\System32\Dism.exe"
$bootWim = Join-Path $mediaDir 'sources\boot.wim'
$mountDir = Join-Path $peRoot 'mount'
Info "挂载 boot.wim（自动选择 WinPE 索引）"

# WinPE 索引通常为 1（ADK）或 2（ISO 抽取）。尝试 index:1，失败则 index:2。
$indexUsed = 1
& $dism /Mount-Image /ImageFile:"$bootWim" /Index:1 /MountDir:"$mountDir" 2>$null
if ($LASTEXITCODE -ne 0) {
    $indexUsed = 2
    & $dism /Mount-Image /ImageFile:"$bootWim" /Index:2 /MountDir:"$mountDir"
    if ($LASTEXITCODE -ne 0) { Fail "挂载 boot.wim 失败" }
}
Info "使用索引：$indexUsed"

$startnet = Join-Path $mountDir 'Windows\System32\startnet.cmd'
@'
@echo off
wpeinit
echo (lanpe) 正在启动维护环境...
if exist X:\LanPE\launcher\PELauncher.exe (
    X:\LanPE\launcher\PELauncher.exe
) else (
    cmd.exe
)
'@ | Set-Content -Path $startnet -Encoding ASCII

& $dism /Unmount-Image /MountDir:"$mountDir" /Commit
if ($LASTEXITCODE -ne 0) { Fail "DISM 提交失败（$LASTEXITCODE）" }

$wimlib = Get-Command wimlib-imagex.exe -ErrorAction SilentlyContinue
if ($wimlib) {
    Info "wimlib 压缩 boot.wim"
    & $wimlib.Source optimize "$bootWim" --compress=LZX:100
}

$zip = Join-Path $OutDir 'win10pe-media.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Info "打包 -> $zip"
Compress-Archive -Path (Join-Path $mediaDir '*') -DestinationPath $zip -CompressionLevel Optimal
Info "完成：$zip"
