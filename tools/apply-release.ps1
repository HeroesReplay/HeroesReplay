param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [string]$SecretsFile = 'C:\heroesreplay\secrets\appsettings.secrets.json',
    [int]$WaitForPid = 0
)

$ErrorActionPreference = 'Stop'

if ($InstallDir -match '\\src\\|\\worktrees\\') {
    throw "Refusing to replace a source build at $InstallDir"
}

if ($WaitForPid -gt 0) {
    Wait-Process -Id $WaitForPid -ErrorAction SilentlyContinue
}

$deadline = (Get-Date).AddMinutes(2)
while ((Get-Process heroesreplay -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
}

if (Get-Process heroesreplay -ErrorAction SilentlyContinue) {
    throw 'heroesreplay is still running. The install was not replaced.'
}

$source = $StagingDir
if (-not (Test-Path (Join-Path $source 'heroesreplay.exe'))) {
    $found = Get-ChildItem $StagingDir -Recurse -Filter heroesreplay.exe | Select-Object -First 1
    if (-not $found) {
        throw "No heroesreplay.exe under $StagingDir"
    }
    $source = $found.DirectoryName
}

$previous = "$InstallDir.previous"
if (Test-Path $previous) {
    Remove-Item $previous -Recurse -Force
}

Rename-Item -LiteralPath $InstallDir -NewName (Split-Path $previous -Leaf)
try {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir -Recurse -Force
    Get-ChildItem $InstallDir -Recurse -Filter service.json -ErrorAction SilentlyContinue | Remove-Item -Force
    if ($SecretsFile -and (Test-Path $SecretsFile)) {
        Copy-Item $SecretsFile (Join-Path $InstallDir 'appsettings.secrets.json') -Force
    }
    elseif (Test-Path (Join-Path $previous 'appsettings.secrets.json')) {
        Copy-Item (Join-Path $previous 'appsettings.secrets.json') (Join-Path $InstallDir 'appsettings.secrets.json') -Force
    }
}
catch {
    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
    }
    Rename-Item -LiteralPath $previous -NewName (Split-Path $InstallDir -Leaf)
    throw
}

if (-not (Get-Process obs64 -ErrorAction SilentlyContinue)) {
    $scene = Join-Path $InstallDir 'obs\Default.json'
    if (Test-Path $scene) {
        $sceneDest = Join-Path $env:APPDATA 'obs-studio\basic\scenes\HeroesReplay.json'
        New-Item -ItemType Directory -Force -Path (Split-Path $sceneDest) | Out-Null
        Copy-Item $scene $sceneDest -Force
    }
    $ini = Join-Path $InstallDir 'obs\Default\basic.ini'
    if (Test-Path $ini) {
        $profile = Join-Path $env:APPDATA 'obs-studio\basic\profiles\HeroesReplay\basic.ini'
        New-Item -ItemType Directory -Force -Path (Split-Path $profile) | Out-Null
        Copy-Item $ini $profile -Force
    }
}
else {
    Write-Host 'OBS is open. Scene files in the release were left under obs\ and were not copied.'
}

Remove-Item $previous -Recurse -Force
$env:HEROES_REPLAY_ENV = 'prod'
Start-Process -FilePath (Join-Path $InstallDir 'heroesreplay.exe') -ArgumentList 'services','start' -WorkingDirectory $InstallDir
