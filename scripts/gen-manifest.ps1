<#
.SYNOPSIS
  扫描 release/assets 下的资产，计算 SHA256/size，生成 release/manifest.json。

.DESCRIPTION
  以 release/manifest.template.json（组件元数据，不含 url/hash/size）为骨架，
  结合 -ReleaseTag 计算出每个资产的下载地址与校验值，输出最终清单。

.PARAMETER ReleaseTag
  目标 Release 标签，如 v1.0.0。下载地址据此拼装。

.PARAMETER Repo
  仓库 slug，默认 luolangaga/LanPE。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseTag,

    [string] $Repo = "luolangaga/LanPE",
    [string] $AssetsDir = "$PSScriptRoot\..\release\assets",
    [string] $Template  = "$PSScriptRoot\..\release\manifest.template.json",
    [string] $OutFile   = "$PSScriptRoot\..\release\manifest.json"
)

$ErrorActionPreference = 'Stop'
function Info($m){ Write-Host "[Manifest] $m" -ForegroundColor Cyan }

if (-not (Test-Path $Template)) { throw "找不到清单模板：$Template" }
$assetsDir = (Resolve-Path $AssetsDir).Path
$baseUrl = "https://github.com/$Repo/releases/download/$ReleaseTag"

function Fill-Asset([string]$fileName) {
    if ([string]::IsNullOrWhiteSpace($fileName)) { return $null }
    $p = Join-Path $assetsDir $fileName
    if (-not (Test-Path $p)) {
        Write-Warning "资产缺失：$fileName（将生成占位条目）"
        return [ordered]@{ file = $fileName; url = "$baseUrl/$fileName"; sha256 = ""; sizeBytes = 0 }
    }
    $h = (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLower()
    $s = (Get-Item $p).Length
    Info "$fileName  sha256=$($h.Substring(0,12))…  size=$s"
    return [ordered]@{ file = $fileName; url = "$baseUrl/$fileName"; sha256 = $h; sizeBytes = $s }
}

$tpl = Get-Content $Template -Raw | ConvertFrom-Json

$manifest = [ordered]@{
    schemaVersion = 1
    name          = $tpl.name
    version       = $tpl.version
    releaseTag    = $ReleaseTag
    generatedAt   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    toolchain     = (Fill-Asset 'lanpe-tools.zip')
    bootFiles     = @()
    components    = @()
}

# 共享引导文件（wimboot / bcd / boot.sdi / bootmgr…）
foreach ($bf in $tpl.bootFiles) {
    $manifest.bootFiles += (Fill-Asset $bf.file) | Add-Member -Name 'id' -Value $bf.id -MemberType NoteProperty -Force -PassThru
}

foreach ($c in $tpl.components) {
    $payload = [ordered]@{}
    foreach ($key in $c.payloadFiles.PSObject.Properties.Name) {
        $payload[$key] = Fill-Asset $c.payloadFiles.$key
    }

    $software = @()
    foreach ($sw in $c.software) {
        $asset = Fill-Asset $sw.file
        $software += [ordered]@{
            id              = $sw.id
            name            = $sw.name
            defaultSelected = [bool]$sw.defaultSelected
            file            = $asset.file
            url             = $asset.url
            sha256          = $asset.sha256
            sizeBytes       = $asset.sizeBytes
            archive         = $sw.archive
            extractTo       = $sw.extractTo
        }
    }

    $comp = [ordered]@{
        id              = $c.id
        name            = $c.name
        kind            = $c.kind
        version         = $c.version
        arch            = $c.arch
        description     = $c.description
        defaultSelected = [bool]$c.defaultSelected
        payload         = $payload
        boot            = $c.boot
        options         = $c.options
        software        = $software
    }
    $manifest.components += $comp
}

$json = $manifest | ConvertTo-Json -Depth 12
Set-Content -Path $OutFile -Value $json -Encoding UTF8
Info "已生成：$OutFile"
