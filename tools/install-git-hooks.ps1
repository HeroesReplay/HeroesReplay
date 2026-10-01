# Copies tools/git-hooks into the shared git hooks directory. Worktrees that do not
# yet contain tools/verify.ps1 skip the checks. A build of HeroesReplay.CLI runs this.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

# A git hook exports GIT_DIR. Git then treats the process directory as the work
# tree, and a CLI build started by the hook would look for tools under
# src/HeroesReplay.CLI instead of the repository root.
Remove-Item Env:GIT_DIR, Env:GIT_WORK_TREE, Env:GIT_INDEX_FILE, Env:GIT_PREFIX, Env:GIT_COMMON_DIR -ErrorAction SilentlyContinue

$root = (git rev-parse --show-toplevel).Trim()
if (-not $root) {
    throw 'Run from inside the HeroesReplay git clone.'
}

$common = (git rev-parse --git-common-dir).Trim()
if (-not [System.IO.Path]::IsPathRooted($common)) {
    $common = Join-Path (Get-Location) $common
}
$dest = Join-Path ([System.IO.Path]::GetFullPath($common)) 'hooks'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$utf8 = New-Object System.Text.UTF8Encoding $false
foreach ($name in @('pre-commit', 'pre-push')) {
    $src = Join-Path $root "tools\git-hooks\$name"
    if (-not (Test-Path $src)) {
        throw "Missing $src"
    }
    $text = [System.IO.File]::ReadAllText($src)
    $text = $text -replace "`r`n", "`n" -replace "`r", "`n"
    if (-not $text.EndsWith("`n")) {
        $text += "`n"
    }
    [System.IO.File]::WriteAllText((Join-Path $dest $name), $text, $utf8)
}

Write-Host "Git hooks installed to $dest"
