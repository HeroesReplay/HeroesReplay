param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [string]$SecretsFile = 'C:\heroesreplay\secrets\appsettings.secrets.json',
    [int]$WaitForPid = 0
)

$ErrorActionPreference = 'Stop'
$InstallDir = $InstallDir.TrimEnd('\')
$StagingDir = $StagingDir.TrimEnd('\')

if ($InstallDir -match '\\src\\|\\worktrees\\') {
    throw "Refusing to replace a source build at $InstallDir"
}

function Clear-ServiceStop {
    $stop = Join-Path $env:LOCALAPPDATA 'HeroesReplay\services.stop'
    if (Test-Path -LiteralPath $stop) {
        Remove-Item -LiteralPath $stop -Force
    }
}

function Start-HeroesReplayStack {
    $env:HEROES_REPLAY_ENV = 'prod'
    $schtasks = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    & $schtasks /Query /TN HeroesReplay-live *> $null
    if ($LASTEXITCODE -eq 0) {
        & $schtasks /Run /TN HeroesReplay-live
        if ($LASTEXITCODE -ne 0) {
            throw "schtasks /Run HeroesReplay-live exited $LASTEXITCODE"
        }

        Write-Host 'Started scheduled task HeroesReplay-live.'
        return
    }

    $launcher = Join-Path $env:LOCALAPPDATA 'HeroesReplay\start-live.cmd'
    if (Test-Path -LiteralPath $launcher) {
        Start-Process -FilePath $launcher -WorkingDirectory $InstallDir
        Write-Host "Started $launcher"
        return
    }

    Start-Process -FilePath (Join-Path $InstallDir 'heroesreplay.exe') -ArgumentList 'services', 'start' -WorkingDirectory $InstallDir
    Write-Host 'Started heroesreplay services start.'
}

function Restore-PreviousInstall([string]$Previous) {
    if (-not (Test-Path -LiteralPath $Previous)) {
        return
    }

    # robocopy exit codes 0-7 are success. Mirror so a partial copy does not leave new files behind.
    & robocopy.exe $Previous $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy $Previous -> $InstallDir failed with exit $LASTEXITCODE"
    }
}

if ($WaitForPid -gt 0) {
    Wait-Process -Id $WaitForPid -ErrorAction SilentlyContinue
}

$deadline = (Get-Date).AddMinutes(2)
while ((Get-Process heroesreplay -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
}

if (Get-Process heroesreplay -ErrorAction SilentlyContinue) {
    Clear-ServiceStop
    Write-Host 'heroesreplay is still running. The install was not replaced. services.stop was cleared so the current build can keep running.'
    exit 1
}

$source = $StagingDir
$stagedExe = Join-Path $source 'heroesreplay.exe'
if (-not (Test-Path -LiteralPath $stagedExe)) {
    $found = $null
    if (Test-Path -LiteralPath $StagingDir) {
        $found = Get-ChildItem -LiteralPath $StagingDir -Recurse -Filter heroesreplay.exe | Select-Object -First 1
    }

    if (-not $found) {
        Write-Host "No heroesreplay.exe under $StagingDir"
        Clear-ServiceStop
        Start-HeroesReplayStack
        exit 1
    }

    $source = $found.DirectoryName
}

$previous = "$InstallDir.previous"
if (Test-Path -LiteralPath $previous) {
    Remove-Item -LiteralPath $previous -Recurse -Force
}

# cmd /k consoles keep this directory as their cwd, so renaming the folder fails while those windows are open. Overwriting the files does not.
try {
    Copy-Item -LiteralPath $InstallDir -Destination $previous -Recurse -Force
}
catch {
    Write-Host "Could not back up the install: $($_.Exception.Message)"
    Clear-ServiceStop
    Start-HeroesReplayStack
    exit 1
}

try {
    Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir -Recurse -Force
    Get-ChildItem $InstallDir -Recurse -Filter service.json -ErrorAction SilentlyContinue | Remove-Item -Force
    if ($SecretsFile -and (Test-Path -LiteralPath $SecretsFile)) {
        Copy-Item -LiteralPath $SecretsFile -Destination (Join-Path $InstallDir 'appsettings.secrets.json') -Force
    }
    elseif (Test-Path -LiteralPath (Join-Path $previous 'appsettings.secrets.json')) {
        Copy-Item -LiteralPath (Join-Path $previous 'appsettings.secrets.json') -Destination (Join-Path $InstallDir 'appsettings.secrets.json') -Force
    }
}
catch {
    Write-Host "Release copy failed: $($_.Exception.Message)"
    try {
        Restore-PreviousInstall $previous
    }
    catch {
        Write-Host "Could not restore the previous install: $($_.Exception.Message)"
    }

    Clear-ServiceStop
    Start-HeroesReplayStack
    exit 1
}

try {
    if (-not (Get-Process obs64 -ErrorAction SilentlyContinue)) {
        $scene = Join-Path $InstallDir 'obs\Default.json'
        if (Test-Path -LiteralPath $scene) {
            $sceneDest = Join-Path $env:APPDATA 'obs-studio\basic\scenes\HeroesReplay.json'
            New-Item -ItemType Directory -Force -Path (Split-Path $sceneDest) | Out-Null
            Copy-Item -LiteralPath $scene -Destination $sceneDest -Force
        }

        $ini = Join-Path $InstallDir 'obs\Default\basic.ini'
        if (Test-Path -LiteralPath $ini) {
            $profile = Join-Path $env:APPDATA 'obs-studio\basic\profiles\HeroesReplay\basic.ini'
            New-Item -ItemType Directory -Force -Path (Split-Path $profile) | Out-Null
            Copy-Item -LiteralPath $ini -Destination $profile -Force
        }
    }
    else {
        Write-Host 'OBS is open. Scene files in the release were left under obs\ and were not copied.'
    }
}
catch {
    Write-Host "OBS scene files were not copied: $($_.Exception.Message)"
}

Clear-ServiceStop
try {
    Start-HeroesReplayStack
}
catch {
    Write-Host "Could not start HeroesReplay: $($_.Exception.Message)"
    exit 1
}

if (Test-Path -LiteralPath $previous) {
    Remove-Item -LiteralPath $previous -Recurse -Force
}
