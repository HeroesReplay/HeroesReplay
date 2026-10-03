# Creates C:\heroesreplay folders, copies the OBS collection from this clone, installs the
# OBS profile template only when the machine has no profile of that name, then fills
# gitignored secrets from 1Password. Scene paths in Default.json are relative to obs\.
# heroesreplay rewrites the live collection to this folder when OBS is closed. Pass the
# names when OBS:ProfileName or OBS:SceneCollectionName is not HeroesReplay on this machine.
# See AGENTS.md and .agents/skills/op-service-account/SKILL.md.
param(
    [string]$ProfileName = 'HeroesReplay',
    [string]$SceneCollectionName = 'HeroesReplay'
)

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
$profileDir = Join-Path $obsBasic "profiles\$ProfileName"
$collectionFile = Join-Path $scenesDir "$SceneCollectionName.json"
$profileIni = Join-Path $profileDir 'basic.ini'
$utf8 = New-Object System.Text.UTF8Encoding $false
if (Get-Process obs64 -ErrorAction SilentlyContinue) {
    Write-Warning 'OBS is running. The live collection and profile were not touched.'
}
else {
    # An existing collection may hold the operator's scenes. heroesreplay updates one it manages
    # (services start, after a backup under %LOCALAPPDATA%\HeroesReplay\obs\backups) and keeps a custom one.
    if (Test-Path -LiteralPath $collectionFile) {
        Write-Host "OBS collection kept: $collectionFile already exists. services start updates it when HeroesReplay manages it."
    }
    else {
        New-Item -ItemType Directory -Force -Path $scenesDir | Out-Null
        $collection = [System.IO.File]::ReadAllText((Join-Path $root 'obs\Default.json'))
        if ($SceneCollectionName -ne 'HeroesReplay') {
            # OBS lists a collection by its top-level name. Default.json opens with that property.
            $collection = ([regex]'"name"\s*:\s*"[^"]*"').Replace($collection, '"name": ' + ($SceneCollectionName | ConvertTo-Json), 1)
        }

        # Written beside the target and then moved, so OBS never finds half a file.
        $temp = "$collectionFile.$([guid]::NewGuid().ToString('N')).tmp"
        [System.IO.File]::WriteAllText($temp, $collection, $utf8)
        Move-Item -LiteralPath $temp -Destination $collectionFile
        Write-Host "OBS collection -> $collectionFile"
        Write-Host 'Scene paths are relative to obs\. heroesreplay rewrites the live collection when OBS is closed.'
    }

    # The profile (encoder, bitrate, resolution, stream service) belongs to this machine.
    # The packaged basic.ini is only a starting template.
    if (Test-Path -LiteralPath $profileIni) {
        Write-Host "OBS profile kept: $profileIni already exists and was not overwritten."
    }
    else {
        New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
        $ini = [System.IO.File]::ReadAllText((Join-Path $root 'obs\Default\basic.ini'))
        $ini = ([regex]'(?m)^Name=[^\r\n]*').Replace($ini, "Name=$ProfileName", 1)
        $temp = "$profileIni.$([guid]::NewGuid().ToString('N')).tmp"
        [System.IO.File]::WriteAllText($temp, $ini, $utf8)
        Move-Item -LiteralPath $temp -Destination $profileIni
        Write-Host "OBS profile    -> $profileIni (template). Run the OBS Auto-Configuration Wizard and set the stream service in OBS."
    }
}
Write-Host 'Do not copy service.json (stream key). Enable Tools > WebSocket Server Settings on port 4455.'
Write-Host 'Twitch ingest also needs this machine armed: heroesreplay obs arm (stream PC only).'

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
