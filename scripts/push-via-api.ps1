<#
.SYNOPSIS
  用 GitHub Contents API 上传仓库文件（绕开被阻断的 git/ssh 传输层）。
#>
[CmdletBinding()]
param(
    [string] $Repo = 'luolangaga/LanPE',
    [string] $Branch = 'main',
    [string] $Root = (Split-Path -Parent $PSScriptRoot),
    [string] $MessageFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'COMMIT_MSG.txt')
)

$ErrorActionPreference = 'Stop'
$msg = if (Test-Path $MessageFile) { (Get-Content $MessageFile -Raw).Trim() } else { 'chore: sync via API' }

$files = Get-ChildItem -Path $Root -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|publish|\.research|\.build|\.commandcode)\\' } |
    Where-Object { $_.FullName -notmatch '\\release\\assets\\(?!\.gitkeep)' }

"共 $($files.Count) 个文件"

$ok = 0; $fail = 0; $skipped = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')

    # 已存在则带上 sha 才能更新（Contents API 要求），否则视为新增
    $sha = $null
    try { $sha = gh api "/repos/$Repo/contents/$rel`?ref=$Branch" --jq '.sha' 2>$null } catch { }
    if ($sha) { $sha = $sha.Trim() }
    if ($sha -match '^\s*$') { $sha = $null }

    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
    $payloadObj = @{ message = $msg; content = $b64; branch = $Branch }
    if ($sha) { $payloadObj['sha'] = $sha }

    # 内容未变化则跳过，避免产生空提交
    if ($sha) {
        try {
            $remote = gh api "/repos/$Repo/contents/$rel`?ref=$Branch" --jq '.content' 2>$null | Out-String
            $remote = ($remote -replace '\s', '').Trim()
            if ($remote -and $remote -eq $b64) { "SKIP $rel (unchanged)"; $skipped++; continue }
        }
        catch { }
    }

    $payload = $payloadObj | ConvertTo-Json -Compress -Depth 3
    $tmp = Join-Path $env:TEMP ("lanpe_up_" + [guid]::NewGuid().ToString('N') + ".json")
    [IO.File]::WriteAllText($tmp, $payload)

    try {
        $out = gh api -X PUT "/repos/$Repo/contents/$rel" --input $tmp 2>&1 | Out-String
        if ($out -match '"commit"') { "OK   $rel"; $ok++ }
        else { "SKIP $rel"; $skipped++ }
    }
    catch {
        $e = $_.Exception.Message
        if ($e -match 'already exists|sha') { "SKIP $rel (exists)"; $skipped++ }
        else { "FAIL $rel -- $e"; $fail++ }
    }
    finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

"完成：新增 $ok，跳过 $skipped，失败 $fail"
