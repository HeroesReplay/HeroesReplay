# Creates C:\heroesreplay folders, copies the OBS collection from this clone, installs the
# OBS profile template only when the machine has no profile of that name, then fills
# gitignored secrets from 1Password. Scene paths in Default.json are relative to obs\.
# heroesreplay rewrites the live collection to this folder when OBS is closed. Pass the
# names when OBS:ProfileName or OBS:SceneCollectionName is not HeroesReplay on this machine.
# It also fetches the pinned HeroesClientSDK package into .packages (tools/restore-sdk-package.ps1)
# and notes when no installed .NET SDK satisfies the one global.json pins.
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
Write-Host 'Twitch ingest also needs this machine armed: heroesreplay obs arm (the stream PC; a dev box only for a stream proof to its own Twitch account).'

$fill = Join-Path $root 'tools\fill-secrets-from-op.ps1'
if (Test-Path $fill) {
    & $fill
}

# HeroesClientSDK restores from .packages (nuget.config): the public GitHub Release asset, no
# credentials, checked against the SHA-256 pinned in Directory.Packages.props. The build also
# fetches it when it is missing.
$sdk = Join-Path $root 'tools\restore-sdk-package.ps1'
if (Test-Path $sdk) {
    try {
        & $sdk -Root $root
    }
    catch {
        Write-Warning "HeroesClientSDK was not fetched: $($_.Exception.Message)"
        Write-Warning 'Run pwsh -File tools/restore-sdk-package.ps1 before dotnet restore.'
    }
}

# global.json pins the .NET SDK (#462) with rollForward latestPatch: any SDK of the same feature
# band (10.0.4xx for 10.0.402) at or above the pin builds. Dependabot moves the pin; this only
# notes when this machine has no SDK that satisfies it.
$pin = "$((Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version)"
$pinned = [version]$pin
$band = [math]::Floor($pinned.Build / 100)
$sdks = @(dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split '\s+', 2)[0] } | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_ })
$matching = @($sdks | Where-Object { $_.Major -eq $pinned.Major -and $_.Minor -eq $pinned.Minor -and [math]::Floor($_.Build / 100) -eq $band -and $_.Build -ge $pinned.Build })
if ($matching.Count -gt 0) {
    Write-Host ".NET SDK $(($matching | Sort-Object -Descending)[0]) satisfies global.json ($pin, latestPatch)."
}
else {
    Write-Warning ".NET SDK $pin (global.json) is not installed. Installed: $(if ($sdks) { $sdks -join ', ' } else { 'none' })."
    Write-Warning "Install it: winget install --id Microsoft.DotNet.SDK.$($pinned.Major) --version $pin, or the SDK $pin installer from https://dotnet.microsoft.com/download/dotnet/$($pinned.Major).$($pinned.Minor)."
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
