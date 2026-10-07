<#
.SYNOPSIS
  生产三套系统载荷到 release/assets/（无需管理员权限）。

.DESCRIPTION
  采用 wimboot 模型（FirPE / Ventoy 同款）：PE 以裸 WIM 分发，引导时由 wimboot 动态构造
  ramdisk 并加载，从而让多个 PE 共存于同一分区。这是多 PE 共存的标准做法 —— 直接链式
  引导多个 bootmgr 不可行，因为 bootmgr 会到卷根查找固定的 \boot\bcd 与 \sources\boot.wim。

  产物（均放入 release/assets/）：
    win11pe.wim        Win11 PE 主体（源：FirPE 的 11PEX64.WIM）
    win10pe.wim        Win10 PE 主体（源：Win10 ISO 的 sources\boot.wim）
    systemrescue.iso   Linux 救援 ISO
    wimboot            wimboot 引导器（BIOS 与 UEFI 通用）
    boot.sdi / bcd     wimboot 所需共享引导文件
    bootmgr / bootmgr.efi  Windows 引导管理器（共享）

.PARAMETER Win11WimIndex / Win10WimIndex
  传给 wimboot 的 WIM 内部镜像索引（FirPE 与 Win10 均为 2）。
#>
[CmdletBinding()]
param(
    [string] $FirPeIso   = 'D:\Download\FirPE-V2.1.1.iso',
    [string] $Win10Iso   = 'D:\Download\WindowsImages\zh-cn_windows_10_consumer_editions_version_22h2_updated_oct_2025_x64_dvd_38efd00d.iso',
    [string] $OutDir     = "$PSScriptRoot\..\release\assets",
    [int]    $Win11WimIndex = 2,
    [int]    $Win10WimIndex = 2,
    [string] $WimbootUrl = 'https://github.com/ipxe/wimboot/releases/download/v2.9.0/wimboot',
    [switch] $SkipWin11,
    [switch] $SkipWin10,
    [switch] $SkipLinux,
    [switch] $SkipWimboot
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Payload] $m" -ForegroundColor Cyan }
function Warn($m){ Write-Host "[Payload] $m" -ForegroundColor Yellow }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Mount-Iso([string]$path) { (Mount-DiskImage -ImagePath (Resolve-Path $path) -PassThru | Get-Volume).DriveLetter + ':' }
function Dismount-Iso([string]$path) { Dismount-DiskImage -ImagePath (Resolve-Path $path) -ErrorAction SilentlyContinue | Out-Null }

# ---------- 1) Win11 PE ----------
if (-not $SkipWin11) {
    Info "提取 Win11 PE（源：FirPE）"
    $dl = Mount-Iso $FirPeIso
    try {
        $src = Join-Path $dl 'BOOT\11PEX64.WIM'
        if (-not (Test-Path $src)) { throw "未找到 $src" }
        Copy-Item $src (Join-Path $OutDir 'win11pe.wim') -Force
        Info "win11pe.wim ($([math]::Round((Get-Item $src).Length/1MB,1)) MB)"

        # 共享引导文件（wimboot 需要）
        foreach ($pair in @(
            @{ s = 'BOOT\BOOT.SDI';  d = 'boot.sdi' },
            @{ s = 'BOOT\GRUB\BCD';  d = 'bcd' },
            @{ s = 'BOOTMGR';        d = 'bootmgr' },
            @{ s = 'BOOTMGR.EFI';    d = 'bootmgr.efi' })) {
            $sp = Join-Path $dl $pair.s
            if (Test-Path $sp) { Copy-Item $sp (Join-Path $OutDir $pair.d) -Force; Info "  $($pair.d)" }
            else { Warn "  缺少 $($pair.s)" }
        }
    }
    finally { Dismount-Iso $FirPeIso }
}

# ---------- 2) Win10 PE ----------
if (-not $SkipWin10) {
    Info "提取 Win10 PE（源：Win10 ISO）"
    $dl = Mount-Iso $Win10Iso
    try {
        $src = Join-Path $dl 'sources\boot.wim'
        if (-not (Test-Path $src)) { throw "未找到 $src" }
        Copy-Item $src (Join-Path $OutDir 'win10pe.wim') -Force
        Info "win10pe.wim ($([math]::Round((Get-Item $src).Length/1MB,1)) MB)"
    }
    finally { Dismount-Iso $Win10Iso }
}

# ---------- 3) SystemRescue ----------
if (-not $SkipLinux) {
    $out = Join-Path $OutDir 'systemrescue.iso'
    if (Test-Path $out) { Info "systemrescue.iso 已存在，跳过" }
    else {
        Info "下载 SystemRescue ISO…"
        try {
            $page = (Invoke-WebRequest 'https://www.system-rescue.org/Download/' -UseBasicParsing).Content
            $m = [regex]::Match($page, 'href="(?<u>[^"]*systemrescue-[0-9.]+-amd64\.iso)"')
            if ($m.Success) {
                $url = $m.Groups['u'].Value
                if ($url -notmatch '^https?://') { $url = ([uri]::new([uri]'https://www.system-rescue.org/Download/', $url)).AbsoluteUri }
                Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
                Info "systemrescue.iso  SHA256=$((Get-FileHash $out -Algorithm SHA256).Hash.ToLower())"
            } else { Warn "无法解析直链，请手动放置 systemrescue.iso" }
        } catch { Warn "下载失败：$($_.Exception.Message)" }
    }
}

# ---------- 4) wimboot ----------
if (-not $SkipWimboot) {
    $w = Join-Path $OutDir 'wimboot'
    if (-not (Test-Path $w)) {
        try { Invoke-WebRequest -Uri $WimbootUrl -OutFile $w -UseBasicParsing; Info "wimboot 下载完成" }
        catch { Warn "wimboot 下载失败：$($_.Exception.Message)" }
    }
}

Info "载荷清单："
Get-ChildItem $OutDir -File | Where-Object { $_.Name -match 'win1[01]pe\.wim|systemrescue\.iso|wimboot|boot\.sdi|bcd|bootmgr' } |
    Select-Object Name,@{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
Info "完成。"
