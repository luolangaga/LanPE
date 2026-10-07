<#
.SYNOPSIS
  从 FirPE ISO 提取便携工具与引导工具，打包为 release/assets/apps/*.zip。

.DESCRIPTION
  步骤：
   1. 挂载 FirPE ISO
   2. 用 7z 从 BOOT\11PEX64.WIM（Win11 PE，镜像索引 2）提取各分类便携工具
   3. 从 ISO 提取 GRUB/DOS 引导工具（BOOT\IMGS、BOOT\GRUB）
   4. 按分类打包为 apps/*.zip，供打包器分发

.PARAMETER IsoPath
  FirPE ISO 路径。
#>
[CmdletBinding()]
param(
    [string] $IsoPath  = 'D:\Download\FirPE-V2.1.1.iso',
    [string] $WorkRoot = "$PSScriptRoot\..\.build\firpe",
    [string] $OutDir   = "$PSScriptRoot\..\release\assets\apps",
    [string] $SevenZip = '7z'
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Extract] $m" -ForegroundColor Cyan }
function Warn($m){ Write-Host "[Extract] $m" -ForegroundColor Yellow }

if (-not (Test-Path $IsoPath)) { throw "找不到 FirPE ISO：$IsoPath" }
New-Item -ItemType Directory -Force -Path $WorkRoot, $OutDir | Out-Null

# --- 1) 挂载 ISO ---
Info "挂载 ISO：$IsoPath"
$img = Mount-DiskImage -ImagePath (Resolve-Path $IsoPath) -PassThru
$drive = ($img | Get-Volume).DriveLetter + ':'
Info "已挂载到 $drive"

try {
    $wim = "$drive\BOOT\11PEX64.WIM"
    if (-not (Test-Path $wim)) { throw "ISO 内未找到 BOOT\11PEX64.WIM" }

    $extractRoot = Join-Path $WorkRoot 'wim'
    if (Test-Path $extractRoot) { Remove-Item $extractRoot -Recurse -Force }

    # --- 2) 提取便携工具分类目录 ---
    # WIM 镜像索引 2 内的路径前缀为 "2\"
    $toolGroups = @(
        @{ id = 'disk-tools';    pePath = '2\Program Files (x86)\硬盘工具' },
        @{ id = 'hardware-tools';pePath = '2\Program Files (x86)\硬件检测' },
        @{ id = 'system-tools';  pePath = '2\Program Files (x86)\系统维护' },
        @{ id = 'file-tools';    pePath = '2\Program Files (x86)\文件工具' },
        @{ id = 'backup-tools';  pePath = '2\Program Files (x86)\备份还原' },
        @{ id = 'misc-tools';    pePath = '2\Program Files\Others' }
    )

    foreach ($g in $toolGroups) {
        Info "提取 $($g.id)：$($g.pePath)"
        $dest = Join-Path $extractRoot $g.id
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        & $SevenZip x $wim "-o$dest" $g.pePath -y -bso0 -bsp0 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Warn "7z 提取 $($g.id) 返回 $LASTEXITCODE" }

        # 7z 会保留完整镜像路径，拍平：找到该分类的叶子目录并把内容上移
        $leaf = Join-Path $dest ($g.pePath.Replace('/', '\'))
        if (Test-Path $leaf) {
            Get-ChildItem $leaf -Force | ForEach-Object { Move-Item $_.FullName $dest -Force }
            # 清理残留的嵌套骨架
            Get-ChildItem $dest -Directory | Where-Object { $_.Name -in @('2','Program Files','Program Files (x86)') } |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # --- 3) 提取 GRUB/DOS 引导工具（来自 ISO 根，非 WIM） ---
    foreach ($sub in @('BOOT\IMGS', 'BOOT\GRUB')) {
        $src = "$drive\$sub"
        if (Test-Path $src) {
            $id = 'boot-' + ($sub.Split('\')[-1]).ToLower()
            Info "提取 $id：$src"
            $dest = Join-Path $extractRoot $id
            New-Item -ItemType Directory -Force -Path $dest | Out-Null
            Copy-Item "$src\*" $dest -Recurse -Force
        }
    }

    # --- 4) 打包为 apps/*.zip ---
    Info "打包到 $OutDir"
    Get-ChildItem $extractRoot -Directory | ForEach-Object {
        $zip = Join-Path $OutDir ($_.Name + '.zip')
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $_.FullName '*') -DestinationPath $zip -CompressionLevel Optimal
        Info "  $($_.Name).zip  ($([math]::Round((Get-Item $zip).Length/1MB,2)) MB)"
    }
}
finally {
    Info "卸载 ISO"
    Dismount-DiskImage -ImagePath (Resolve-Path $IsoPath) | Out-Null
}

Info "完成。产物在 $OutDir"
