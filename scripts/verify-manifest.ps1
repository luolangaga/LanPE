<#
.SYNOPSIS
    校验 release/manifest.json 中所有 URL 是否能在 GitHub Release 资产中找到。
.DESCRIPTION
    过去踩过的坑：GitHub Release 下载链接用的是「资产名」而非本地暂存路径。
    apps/disk-tools.zip 这类带目录前缀的值会 404，正确形式是 disk-tools.zip。
    本脚本在发布前跑一遍，确保所有引用可达。
.PARAMETER ReleaseTag
    要校验的 Release 标签，默认 v1.0.0。
.EXAMPLE
    .\scripts\verify-manifest.ps1 -ReleaseTag v1.0.0
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag,
    [string]$Repo = 'luolangaga/LanPE',
    [string]$Manifest = 'release/manifest.json'
)

$ErrorActionPreference = 'Stop'
function Info($m) { Write-Host "[Verify] $m" -ForegroundColor Cyan }

# 相对路径按脚本所在目录解析，保证从任意工作目录调用都能找到仓库内的清单
if (-not [System.IO.Path]::IsPathRooted($Manifest)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Manifest = Join-Path $repoRoot $Manifest
}
if (-not (Test-Path $Manifest)) { throw "找不到清单：$Manifest" }

$m = Get-Content $Manifest -Raw | ConvertFrom-Json
$actual = @(gh release view $ReleaseTag --repo $Repo --json assets -q '.assets[].name')
if (-not $actual -or $actual.Count -eq 0) { throw "Release $ReleaseTag 上没有资产。" }
Info "Release 资产数：$($actual.Count)"

$urls = [System.Collections.ArrayList]::new()
if ($m.toolchain) { [void]$urls.Add($m.toolchain.url) }
foreach ($b in $m.bootFiles) { [void]$urls.Add($b.url) }
foreach ($c in $m.components) {
    foreach ($p in $c.payload.PSObject.Properties.Name) {
        $v = $c.payload.$p
        if ($v -and $v.url) { [void]$urls.Add($v.url) }
    }
    foreach ($s in $c.software) { [void]$urls.Add($s.url) }
}

Info "清单引用 URL 数：$($urls.Count)"

$missing = [System.Collections.ArrayList]::new()
$malformed = [System.Collections.ArrayList]::new()

foreach ($u in $urls) {
    if ([string]::IsNullOrWhiteSpace($u)) { continue }
    if ($u -notmatch "/releases/download/$([regex]::Escape($ReleaseTag))/") {
        [void]$malformed.Add($u)
        continue
    }
    $name = Split-Path -Leaf $u
    if ($actual -notcontains $name) { [void]$malformed.Add("(资产缺失) $u") }
}

if ($malformed.Count -gt 0) {
    Write-Host ""
    Write-Host "发现 $($malformed.Count) 个不可达引用：" -ForegroundColor Red
    $malformed | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "提示：Release 下载 URL 必须用资产名（不含 apps/ 等目录前缀）。" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "OK - 全部 $($urls.Count) 个引用均可解析，无 404 风险。" -ForegroundColor Green
exit 0
