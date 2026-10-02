param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [string]$SecretsFile = 'C:\heroesreplay\secrets\appsettings.secrets.json',
    [int]$WaitForPid = 0
)

$ErrorActionPreference = 'Stop'
$InstallDir = $InstallDir.TrimEnd('\')
$StagingDir = $StagingDir.TrimEnd('\')

# This runs in a hidden window. Keep what it decided (OBS files, stream arm) for the operator.
try {
    $logDir = Join-Path $env:LOCALAPPDATA 'HeroesReplay\logs'
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Start-Transcript -Path (Join-Path $logDir 'apply-release.log') -Append | Out-Null
}
catch {
    Write-Host "Could not start the update log: $($_.Exception.Message)"
}

if ($InstallDir -match '\\src\\|\\worktrees\\') {
    throw "Refusing to replace a source build at $InstallDir"
}

# The environment of the stack this update replaces and restarts. Start-HeroesReplayStack starts prod.
$environment = if ($env:HEROES_REPLAY_ENV) { $env:HEROES_REPLAY_ENV } else { 'prod' }

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

function Protect-MinReplayId([string]$Exe, [string]$PreviousSettings, [string]$TargetSettings) {
    # The zip being installed knows this command. The copy already on the machine may not.
    if (-not (Test-Path -LiteralPath $Exe)) {
        Write-Host 'The staged heroesreplay.exe is missing. MinReplayId was not preserved.'
        return
    }

    if (-not (Test-Path -LiteralPath $PreviousSettings) -or -not (Test-Path -LiteralPath $TargetSettings)) {
        return
    }

    try {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $Exe
        $start.Arguments = "update preserve-min-replay-id --previous `"$PreviousSettings`" --target `"$TargetSettings`""
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $preserve = New-Object System.Diagnostics.Process
        $preserve.StartInfo = $start
        if (-not $preserve.Start()) {
            Write-Host 'MinReplayId preserve did not start. The staged appsettings.json was left unchanged.'
            return
        }

        $preserve.WaitForExit()
        if ($preserve.ExitCode -ne 0) {
            Write-Host "MinReplayId preserve exited $($preserve.ExitCode). The staged appsettings.json was left unchanged."
        }
    }
    catch {
        Write-Host "MinReplayId was not preserved: $($_.Exception.Message)"
    }
}

function Invoke-ReleaseCommand([string]$Exe, [string[]]$Arguments, [string]$What) {
    if (-not (Test-Path -LiteralPath $Exe)) {
        Write-Host "$What was skipped: $Exe is missing."
        return
    }

    # Native stderr must not become a terminating error under Stop in Windows PowerShell.
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "$What exited $LASTEXITCODE."
        }
    }
    catch {
        Write-Host "$What failed: $($_.Exception.Message)"
    }
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

Protect-MinReplayId (Join-Path $source 'heroesreplay.exe') (Join-Path $InstallDir 'appsettings.json') (Join-Path $source 'appsettings.json')

# Twitch ingest now also needs the machine-local arm (%LOCALAPPDATA%\HeroesReplay\stream-armed).
# Once per machine, before the files are replaced: arm it only when the install being replaced
# streams (effective OBS:StreamingEnabled true), so production stays live. Never arms otherwise.
Invoke-ReleaseCommand (Join-Path $source 'heroesreplay.exe') @('update', 'migrate-stream-arm', '--previous', $InstallDir, '--environment', $environment) 'Stream arm migration'

$previous = "$InstallDir.previous"
$backUpInstall = $true
if (Test-Path -LiteralPath $previous) {
    $roleFile = Join-Path $InstallDir 'role-ready.txt'
    $healthExe = Join-Path $source 'heroesreplay.exe'
    & $healthExe update release-health --role-file $roleFile
    if ($LASTEXITCODE -ne 0) {
        # The services are already stopped for this update. Exiting here left the live stream down.
        # Keep the older proven rollback, do not back up this unproven install, and install the release.
        Write-Host "Previous install is still inside the stabilization window. It stays the rollback; this install is not backed up."
        $backUpInstall = $false
    }
    else {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

# cmd /k consoles keep this directory as their cwd, so renaming the folder fails while those windows are open. Overwriting the files does not.
if ($backUpInstall) {
    try {
        Copy-Item -LiteralPath $InstallDir -Destination $previous -Recurse -Force
    }
    catch {
        Write-Host "Could not back up the install: $($_.Exception.Message)"
        Clear-ServiceStop
        Start-HeroesReplayStack
        exit 1
    }
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

# The new build reads OBS:SceneCollectionName and OBS:ProfileName from this install. While OBS is
# closed it replaces the scene collection. The profile (basic.ini) belongs to the machine: the
# packaged one is only copied when the machine has none, and an existing profile is kept.
# service.json (stream key) is never copied.
Invoke-ReleaseCommand (Join-Path $InstallDir 'heroesreplay.exe') @('update', 'install-obs', '--install', $InstallDir, '--environment', $environment) 'OBS scene files'

Clear-ServiceStop
try {
    Start-HeroesReplayStack
}
catch {
    Write-Host "Could not start HeroesReplay: $($_.Exception.Message)"
    exit 1
}

if (Test-Path -LiteralPath $previous) {
    Write-Host "Previous install kept at $previous until the new stack has been healthy."
}
