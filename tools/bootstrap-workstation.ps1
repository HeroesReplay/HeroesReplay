# Creates C:\heroesreplay folders, copies the OBS collection and profile from this
# clone, then fills gitignored secrets from 1Password. Scene paths in Default.json
# are relative to obs\. heroesreplay rewrites the live collection to this folder
# when OBS is closed. See AGENTS.md and .grok/skills/op-service-account/SKILL.md.
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
if (-not $root) {
    throw 'Run from inside the HeroesReplay git clone.'
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
if (Get-Process obs64 -ErrorAction SilentlyContinue) {
    Write-Warning 'OBS is running. The live collection and profile were not overwritten.'
}
else {
    Copy-Item -Force (Join-Path $root 'obs\Default.json') (Join-Path $scenesDir 'HeroesReplay.json')
    Copy-Item -Force (Join-Path $root 'obs\Default\basic.ini') (Join-Path $profileDir 'basic.ini')
    Write-Host "OBS collection -> $scenesDir\HeroesReplay.json"
    Write-Host "OBS profile    -> $profileDir\basic.ini"
    Write-Host 'Scene paths are relative to obs\. heroesreplay rewrites the live collection when OBS is closed.'
}
Write-Host 'Do not copy service.json (stream key). Enable Tools → WebSocket Server on port 4455.'

$fill = Join-Path $root 'tools\fill-secrets-from-op.ps1'
if (Test-Path $fill) {
    & $fill
}

$hooks = Join-Path $root 'tools\install-git-hooks.ps1'
if (Test-Path $hooks) {
    & $hooks
}

Write-Host 'Layout:'
Write-Host '  C:\heroesreplay\HeroesReplay     git clone'
Write-Host '  C:\heroesreplay\Battle.net       Battle.net.exe (winget --location)'
Write-Host '  C:\heroesreplay\Replays          spectate file queue'
Write-Host '  C:\heroesreplay\Data\Standard    Heroes Profile downloads'
Write-Host '  C:\heroesreplay\Data\Requests    Twitch-requested downloads'
Write-Host '  C:\heroesreplay\Data\Contexts    OBS recordings + per-replay context'
Write-Host 'Quit HotS, then: heroesreplay client configure'
Write-Host 'Once, from an elevated shell, after each new Heroes build: heroesreplay client firewall'
Write-Host 'Spectate and the other services run unelevated.'
