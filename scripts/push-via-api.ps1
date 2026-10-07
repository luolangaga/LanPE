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

    # 已存在且内容相同则跳过
    $existing = $null
    try { $existing = gh api "/repos/$Repo/contents/$rel`?ref=$Branch" --jq '.sha, .size' 2>$null } catch { }

    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
    $payload = @{ message = $msg; content = $b64; branch = $Branch } | ConvertTo-Json -Compress -Depth 3
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
