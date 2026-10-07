<#
.SYNOPSIS
  获取并打包 LanPE 工具链 -> release/assets/lanpe-tools.zip。

.DESCRIPTION
  从 a1ive/grub（GRUB2 Windows 构建）下载 grub2-latest.tar.gz，解包出
  grub-mkimage.exe 与 i386-pc / x86_64-efi 模块目录，打包为 lanpe-tools.zip。

  注意：该包不含 grub-mkrescue / xorriso。ISO 由打包器内置的
  Iso9660Writer（纯 C#）写出，引导镜像由 grub-mkimage 生成，因此不再需要它们。

.PARAMETER KeepTmp  保留解压临时目录便于排查。
#>
[CmdletBinding()]
param(
    [string] $OutDir = "$PSScriptRoot\..\release\assets",
    [string] $TmpDir = (Join-Path $env:TEMP "lanpe_tools_build"),
    [switch] $KeepTmp
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Tools] $m" -ForegroundColor Cyan }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Remove-Item $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

# --- 1) 下载 GRUB2 Windows 构建 ---
Info "下载 GRUB2 (a1ive/grub)…"
$tarGz = Join-Path $TmpDir 'grub2-latest.tar.gz'
gh release download -R a1ive/grub -p 'grub2-latest.tar.gz' -D $TmpDir
if (-not (Test-Path $tarGz)) { throw "下载失败：$tarGz（需要 gh 且能访问 GitHub API）" }
Info "  $([math]::Round((Get-Item $tarGz).Length/1MB,2)) MB"

# --- 2) 解压 ---
Info "解包…"
& 7z x $tarGz -o"$TmpDir" -y -bso0 -bsp0 | Out-Null
$tar = Join-Path $TmpDir 'grub2-latest.tar'
if (Test-Path $tar) { & 7z x $tar -o"$TmpDir\x" -y -bso0 -bsp0 | Out-Null }

$grubDir = Get-ChildItem $TmpDir -Recurse -Directory -Filter 'grub' | Select-Object -First 1
if (-not $grubDir) { throw "解包后未找到 grub 目录" }
Info "  grub 目录：$($grubDir.FullName)"

# --- 3) 校验关键部件 ---
$mkimage = Get-ChildItem $grubDir.FullName -Filter 'grub-mkimage.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $mkimage) { throw "未找到 grub-mkimage.exe" }

foreach ($arch in @('i386-pc', 'x86_64-efi')) {
    $d = Join-Path $grubDir.FullName $arch
    if (-not (Test-Path $d)) { throw "缺少模块目录：$arch" }
    $n = (Get-ChildItem $d -File).Count
    Info "  $arch : $n 个模块"
}

# --- 4) 组装 tools 目录并打包 ---
$stage = Join-Path $TmpDir 'tools'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $grubDir.FullName (Join-Path $stage 'grub') -Recurse -Force

$zip = Join-Path $OutDir 'lanpe-tools.zip'
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Info "打包 -> $zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Info "  $([math]::Round((Get-Item $zip).Length/1MB,2)) MB"

if (-not $KeepTmp) { Remove-Item $TmpDir -Recurse -Force -ErrorAction SilentlyContinue }

Info "完成。打包器会自动从 Release 下载并解包该工具链。"
