# Same layout on ASA-SERVER (dev) and DESKTOP-8SJE72 (live).
# Creates C:\heroesreplay folders, copies OBS collection/profile from the repo,
# then fills gitignored secrets from 1Password. See AGENTS.md Environments
# and .grok/skills/op-service-account/SKILL.md.
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
if (-not $root) {
    throw 'Run from inside the HeroesReplay git clone.'
}

$expected = 'C:\heroesreplay\HeroesReplay'
$full = [IO.Path]::GetFullPath($root)
if (-not $full.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "Clone is $full. Both machines should use $expected so OBS asset paths in obs/Default.json resolve."
}

$dirs = @(
    'C:\heroesreplay',
    'C:\heroesreplay\Battle.net',
    'C:\heroesreplay\Replays',
    'C:\heroesreplay\Data',
    'C:\heroesreplay\Data\Standard',
    'C:\heroesreplay\Data\Requests',
    'C:\heroesreplay\Data\Contexts',
    'C:\heroesreplay\Data\HeroesData',
    'C:\heroesreplay\secrets'
)
foreach ($d in $dirs) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

$obsBasic = Join-Path $env:APPDATA 'obs-studio\basic'
$scenesDir = Join-Path $obsBasic 'scenes'
$profileDir = Join-Path $obsBasic 'profiles\HeroesReplay'
New-Item -ItemType Directory -Force -Path $scenesDir, $profileDir | Out-Null
Copy-Item -Force (Join-Path $root 'obs\Default.json') (Join-Path $scenesDir 'HeroesReplay.json')
Copy-Item -Force (Join-Path $root 'obs\Default\basic.ini') (Join-Path $profileDir 'basic.ini')
Write-Host "OBS collection -> $scenesDir\HeroesReplay.json"
Write-Host "OBS profile    -> $profileDir\basic.ini"
Write-Host 'Do not copy service.json (stream key). Enable Tools → WebSocket Server on port 4455.'

$fill = Join-Path $root 'tools\fill-secrets-from-op.ps1'
if (Test-Path $fill) {
    & $fill
}

Write-Host 'Layout:'
Write-Host '  C:\heroesreplay\HeroesReplay     git clone'
Write-Host '  C:\heroesreplay\Battle.net       Battle.net.exe (winget --location)'
Write-Host '  C:\heroesreplay\Replays          spectate file queue'
Write-Host '  C:\heroesreplay\Data\Standard    Heroes Profile downloads'
Write-Host '  C:\heroesreplay\Data\Requests    Twitch-requested downloads'
Write-Host '  C:\heroesreplay\Data\Contexts    OBS recordings + per-replay context'
Write-Host 'Quit HotS, then: heroesreplay client configure'
