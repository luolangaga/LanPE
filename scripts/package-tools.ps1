<#
.SYNOPSIS
  把 tools/ 目录打包为 release/assets/lanpe-tools.zip。

.DESCRIPTION
  工具链包含 xorriso / grub-mkrescue(grub2) / wimlib / wimboot / 7za。
  这些二进制体积较大，默认不入库（见 .gitignore）；由本脚本在此处汇总打包。
#>
[CmdletBinding()]
param(
    [string] $ToolsDir = "$PSScriptRoot\..\tools",
    [string] $OutDir   = "$PSScriptRoot\..\release\assets"
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Tools] $m" -ForegroundColor Cyan }

$ToolsDir = (Resolve-Path $ToolsDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zip = Join-Path $OutDir 'lanpe-tools.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }

$required = @('xorriso', 'grub-mkrescue', 'wimlib-imagex', 'wimboot', '7za')
$found = @()
foreach ($name in @('xorriso.exe','grub-mkrescue.bat','grub-mkrescue.sh','grub-mkrescue','wimlib-imagex.exe','wimboot','wimboot.efi','7za.exe','7z.exe')) {
    $hit = Get-ChildItem -Path $ToolsDir -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { $found += $hit.FullName }
}

if ($found.Count -eq 0) { throw "tools 目录为空或未包含所需工具：$ToolsDir" }
Info ("发现工具：" + (($found | ForEach-Object { Split-Path $_ -Leaf }) -join ', '))

Info "打包 -> $zip"
Compress-Archive -Path (Join-Path $ToolsDir '*') -DestinationPath $zip -CompressionLevel Optimal
Info "完成：$zip（$((Get-Item $zip).Length) bytes）"
