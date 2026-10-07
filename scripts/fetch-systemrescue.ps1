<#
.SYNOPSIS
  下载 SystemRescue ISO 并校验 SHA256，保存到 release/assets/systemrescue.iso。

.DESCRIPTION
  从 SystemRescue 官方源下载最新 ISO。若提供 -ExpectedSha256，则强制校验；
  否则打印实际 SHA256 供人工核对（建议随后写入 manifest 模板）。

.NOTES
  官方发布页：https://www.system-rescue.org/Download/
#>
[CmdletBinding()]
param(
    [string] $Url = "https://www.system-rescue.org/Download/",
    [string] $IsoUrl,
    [string] $ExpectedSha256,
    [string] $OutDir = "$PSScriptRoot\..\release\assets"
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Linux] $m" -ForegroundColor Cyan }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir 'systemrescue.iso'

if (-not $IsoUrl) {
    Info "解析下载页以定位最新 amd64 ISO：$Url"
    $html = (Invoke-WebRequest -Uri $Url -UseBasicParsing).Content
    $m = [regex]::Match($html, 'href="(?<u>[^"]*systemrescue-[0-9.]+-amd64\.iso)"')
    if (-not $m.Success) { throw "无法从下载页解析 ISO 直链。请用 -IsoUrl 显式指定。" }
    $IsoUrl = $m.Groups['u'].Value
    if ($IsoUrl -notmatch '^https?://') { $IsoUrl = ([uri]::new([uri]$Url, $IsoUrl)).AbsoluteUri }
}

Info "下载：$IsoUrl"
Invoke-WebRequest -Uri $IsoUrl -OutFile $out -UseBasicParsing

$hash = (Get-FileHash -Path $out -Algorithm SHA256).Hash.ToLower()
Info "SHA256：$hash"
Info "Size  ：$((Get-Item $out).Length) bytes"

if ($ExpectedSha256) {
    if ($hash -ne $ExpectedSha256.ToLower()) {
        Remove-Item $out -Force
        throw "哈希校验失败！期望 $ExpectedSha256，实际 $hash"
    }
    Info "哈希校验通过。"
} else {
    Write-Warning "未提供 -ExpectedSha256，请人工核对后写入清单模板。"
}

Info "完成：$out"
