# Build, enforce unused-code analyzers, and run Category=Unit.
# Integration and smoke tests are not part of this gate.
# pre-commit runs it on every commit. pre-push runs it when the push updates develop,
# and skips when this exact commit tree already passed.
param(
    [ValidateSet('pre-commit', 'pre-push', 'manual')]
    [string]$Hook = 'manual'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:HEROESREPLAY_SKIP_VERIFY -eq '1') {
    Write-Host 'HEROESREPLAY_SKIP_VERIFY=1: skipping dotnet build and unit tests.'
    exit 0
}

Set-Location (git rev-parse --show-toplevel).Trim()

function Get-GitCommonDir {
    $common = (git rev-parse --git-common-dir).Trim()
    if (-not [System.IO.Path]::IsPathRooted($common)) {
        $common = Join-Path (Get-Location) $common
    }
    return [System.IO.Path]::GetFullPath($common)
}

if ($Hook -eq 'pre-push') {
    $payload = ''
    if ([Console]::IsInputRedirected) {
        $payload = [Console]::In.ReadToEnd()
    }
    $stampPath = Join-Path (Get-GitCommonDir) 'heroesreplay-verify-stamp'
    $stamp = ''
    if (Test-Path $stampPath) {
        $stamp = (Get-Content -Raw $stampPath).Trim()
    }
    $needsVerify = $false
    $sawDevelop = $false
    foreach ($line in ($payload -split '\r?\n')) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        $parts = $line.Trim() -split '\s+'
        if ($parts.Length -lt 4) {
            continue
        }
        $localSha = $parts[1]
        $remoteRef = $parts[2]
        if ($remoteRef -ne 'refs/heads/develop') {
            continue
        }
        if ($localSha -eq ('0' * 40)) {
            continue
        }
        $sawDevelop = $true
        $tree = (git rev-parse "$localSha^{tree}").Trim()
        if ($LASTEXITCODE -ne 0 -or $stamp -ne $tree) {
            $needsVerify = $true
        }
    }
    if (-not $sawDevelop) {
        Write-Host 'pre-push: not updating develop; skipping build and unit tests.'
        exit 0
    }
    if (-not $needsVerify) {
        Write-Host 'pre-push: this develop commit already passed dotnet build and unit tests.'
        exit 0
    }
}

Write-Host 'verify: dotnet build heroes-replay.slnx'
dotnet build heroes-replay.slnx --nologo -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet build failed (exit $LASTEXITCODE)."
    Write-Host 'Unused usings and unused private members are build errors (.editorconfig).'
    Write-Host 'If the log says MSB3027, a running heroesreplay.exe has the build locked. Stop it with: heroesreplay services stop'
    exit $LASTEXITCODE
}

Write-Host 'verify: dotnet test heroes-replay.slnx -p:TestCategory=Unit'
dotnet test heroes-replay.slnx --nologo --no-build -c Debug -p:TestCategory=Unit
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet test failed (exit $LASTEXITCODE). The hook runs Category=Unit only, not Integration or Smoke."
    exit $LASTEXITCODE
}

git diff --quiet
if ($LASTEXITCODE -eq 0) {
    $tree = (git write-tree).Trim()
    if ($LASTEXITCODE -eq 0 -and $tree) {
        $stampPath = Join-Path (Get-GitCommonDir) 'heroesreplay-verify-stamp'
        [System.IO.File]::WriteAllText($stampPath, $tree)
        Write-Host "verify: passed. Stamped tree $tree"
    }
}
else {
    Write-Host 'verify: passed. Unstaged changes were present, so the develop push will run this again.'
}

exit 0
