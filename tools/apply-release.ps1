param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [string]$SecretsFile = 'C:\heroesreplay\secrets\appsettings.secrets.json',
    [int]$WaitForPid = 0,
    # The release tag being installed. Empty reads the staged version.txt.
    [string]$Version = '',
    # The stack ran under a supervisor when the update staged. Restart it supervised.
    [switch]$Supervise
)

$ErrorActionPreference = 'Stop'
$InstallDir = $InstallDir.TrimEnd('\')
$StagingDir = $StagingDir.TrimEnd('\')
$stateDir = Join-Path $env:LOCALAPPDATA 'HeroesReplay'

# An older build does not pass -Supervise. The supervisor deletes supervisor.json when the stop file reaches it.
if (-not $Supervise -and (Test-Path -LiteralPath (Join-Path $stateDir 'supervisor.json'))) {
    $Supervise = $true
}

# This runs in a hidden window. Keep what it decided (OBS files, stream arm, health, rollback) for the operator.
try {
    $logDir = Join-Path $stateDir 'logs'
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

# Release tags that failed the health gate here (ReleaseSkipList). ReleaseUpdateGate does not stage them again.
$skipList = Join-Path $stateDir 'updates\skipped-releases.txt'

# A hung health check is unhealthy. update release-health itself stops at Release:HealthWindow.
$healthTimeoutMs = 3 * 60 * 60 * 1000
$script:healthDetail = ''

function Clear-ServiceStop {
    $stop = Join-Path $stateDir 'services.stop'
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

    $launcher = Join-Path $stateDir 'start-live.cmd'
    if (Test-Path -LiteralPath $launcher) {
        Start-Process -FilePath $launcher -WorkingDirectory $InstallDir
        Write-Host "Started $launcher"
        return
    }

    $arguments = @('services', 'start')
    if ($Supervise) {
        $arguments += '--supervise'
    }

    Start-Process -FilePath (Join-Path $InstallDir 'heroesreplay.exe') -ArgumentList $arguments -WorkingDirectory $InstallDir
    Write-Host "Started heroesreplay $($arguments -join ' ')."
}

function Stop-HeroesReplayStack([string]$Exe) {
    # services stop ends the supervisor, then every role, then closes Heroes of the Storm.
    Invoke-ReleaseCommand $Exe @('services', 'stop') 'services stop'
    $deadline = (Get-Date).AddMinutes(2)
    while ((Get-Process heroesreplay -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }

    $left = @(Get-Process heroesreplay -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        Write-Host "heroesreplay is still running after services stop (pid $(($left | ForEach-Object { $_.Id }) -join ', ')). It is killed so the previous install can be restored."
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
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

function Invoke-ReleaseHealth([string]$Exe, [string]$Since) {
    # 0 healthy, 2 unhealthy, 3 a stop was requested, 4 inconclusive. A crash or a hang is unhealthy.
    if (-not (Test-Path -LiteralPath $Exe)) {
        $script:healthDetail = "$Exe is missing."
        return 2
    }

    try {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $Exe
        $start.Arguments = "update release-health --since $Since --install `"$InstallDir`" --environment $environment --wait"
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $check = [System.Diagnostics.Process]::Start($start)
        $stdout = $check.StandardOutput.ReadToEndAsync()
        $stderr = $check.StandardError.ReadToEndAsync()
        if (-not $check.WaitForExit($healthTimeoutMs)) {
            try { $check.Kill() } catch { }
            $script:healthDetail = 'update release-health did not exit.'
            return 2
        }

        $null = $check.WaitForExit()
        $lines = @(($stdout.Result + $stderr.Result) -split "`r?`n" | Where-Object { $_.Trim() })
        $lines | ForEach-Object { Write-Host $_ }
        if ($lines.Count -gt 0) {
            $script:healthDetail = $lines[$lines.Count - 1].Trim()
        }

        return $check.ExitCode
    }
    catch {
        $script:healthDetail = "update release-health failed: $($_.Exception.Message)"
        return 2
    }
}

function Copy-Install([string]$From, [string]$To) {
    # robocopy exit codes 0-7 are success. Mirror so the copy holds exactly one install.
    & robocopy.exe $From $To /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy $From -> $To failed with exit $LASTEXITCODE"
    }
}

function Restore-PreviousInstall([string]$Previous) {
    if (-not (Test-Path -LiteralPath $Previous)) {
        throw "There is no $Previous to restore."
    }

    Copy-Install $Previous $InstallDir
}

function Test-ReleaseSkipped([string]$Tag) {
    if (-not $Tag -or -not (Test-Path -LiteralPath $skipList)) {
        return $false
    }

    try {
        foreach ($line in Get-Content -LiteralPath $skipList) {
            $trimmed = $line.Trim()
            if (-not $trimmed -or $trimmed.StartsWith('#')) {
                continue
            }

            if ((($trimmed -split '\s+', 2)[0]) -ieq $Tag) {
                return $true
            }
        }
    }
    catch {
        Write-Host "Could not read $skipList`: $($_.Exception.Message)"
    }

    return $false
}

function Read-Version([string]$Directory) {
    $file = Join-Path $Directory 'version.txt'
    if (-not (Test-Path -LiteralPath $file)) {
        return ''
    }

    $text = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
    if ($text) {
        return $text.Trim()
    }

    return ''
}

function Add-SkippedRelease([string]$Tag, [string]$Reason) {
    if (-not $Tag) {
        Write-Host 'The release has no version, so it was not added to the skip list.'
        return
    }

    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $skipList) | Out-Null
        $when = (Get-Date).ToUniversalTime().ToString('o')
        $note = ($Reason -replace '\s+', ' ').Trim()
        Add-Content -LiteralPath $skipList -Value "$Tag`t$when`t$note"
        Write-Host "Added $Tag to $skipList. Remove that line to allow it again."
    }
    catch {
        Write-Host "Could not add $Tag to $skipList`: $($_.Exception.Message)"
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

if (-not $Version) {
    $Version = Read-Version $source
}

# A build from before the skip list still stages a release this machine rolled back. Do not install it again.
if (Test-ReleaseSkipped $Version) {
    Write-Host "Release $Version failed its health gate here before ($skipList). It was not installed; the current install starts again."
    Clear-ServiceStop
    Start-HeroesReplayStack
    exit 0
}

Protect-MinReplayId (Join-Path $source 'heroesreplay.exe') (Join-Path $InstallDir 'appsettings.json') (Join-Path $source 'appsettings.json')

# Twitch ingest now also needs the machine-local arm (%LOCALAPPDATA%\HeroesReplay\stream-armed).
# Once per machine, before the files are replaced: arm it only when the install being replaced
# streams (effective OBS:StreamingEnabled true), so production stays live. Never arms otherwise.
Invoke-ReleaseCommand (Join-Path $source 'heroesreplay.exe') @('update', 'migrate-stream-arm', '--previous', $InstallDir, '--environment', $environment) 'Stream arm migration'

# The rollback is always the install being replaced: it just finished a replay, which is what staged
# this update, so it is known good. An older .previous (one an earlier script kept) is overwritten.
# Without a fresh backup the release is not installed.
$previous = "$InstallDir.previous"
try {
    Copy-Install $InstallDir $previous
    Write-Host "Backed up $InstallDir to $previous as the rollback."
}
catch {
    Write-Host "Could not back up the install, so release $Version was not installed: $($_.Exception.Message)"
    Clear-ServiceStop
    Start-HeroesReplayStack
    exit 1
}

# cmd /k consoles keep this directory as their cwd, so renaming the folder fails while those windows are open. Overwriting the files does not.
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
$since = (Get-Date).ToUniversalTime().ToString('o')
try {
    Start-HeroesReplayStack
}
catch {
    Write-Host "Could not start HeroesReplay: $($_.Exception.Message)"
    exit 1
}

# The health gate: every role in services.json heartbeats from a process started after $since, and
# spectate reaches a match clock (or the award screen), inside Release:HealthWindow.
#   0 healthy: .previous becomes this install.
#   4 inconclusive: every role up, but spectate had nothing it could play (empty queue, outage,
#     only held replays). The install stays, .previous is not refreshed, and the tag is not skipped.
#   3 a stop was requested: no verdict.
#   anything else (2, a crash, a hang): a role is down or stale, or every replay spectate tried
#     failed. Roll back.
Write-Host "Release $Version started at $since. Waiting for every role to heartbeat and spectate to reach a match clock."
$health = Invoke-ReleaseHealth (Join-Path $InstallDir 'heroesreplay.exe') $since
if ($health -eq 0) {
    try {
        Copy-Install $InstallDir $previous
        Write-Host "Release $Version is healthy. $previous now holds it as the rollback."
    }
    catch {
        Write-Host "Release $Version is healthy, but $previous was not refreshed: $($_.Exception.Message)"
    }

    exit 0
}

if ($health -eq 4) {
    Write-Host "Release $Version is INCONCLUSIVE, not rolled back: $($script:healthDetail) It stays installed and running. $previous still holds the install it replaced, and the tag was not skipped."
    exit 0
}

if ($health -eq 3) {
    Write-Host "A stop was requested before release $Version was judged. Nothing was rolled back and $previous was kept."
    exit 0
}

Write-Host "Release $Version is unhealthy (release-health exit $health): $($script:healthDetail) Rolling back to $previous."
Add-SkippedRelease $Version "release-health exit $health. $($script:healthDetail)"
Stop-HeroesReplayStack (Join-Path $InstallDir 'heroesreplay.exe')

# The failed install may have moved MinReplayId forward. Keep the higher one after the restore.
$failedSettings = Join-Path ([System.IO.Path]::GetTempPath()) ('heroesreplay-failed-' + [System.IO.Path]::GetRandomFileName() + '.json')
try {
    Copy-Item -LiteralPath (Join-Path $InstallDir 'appsettings.json') -Destination $failedSettings -Force
}
catch {
    Write-Host "The failed install's MinReplayId was not kept: $($_.Exception.Message)"
}

try {
    Restore-PreviousInstall $previous
}
catch {
    Write-Host "Could not restore ${previous}: $($_.Exception.Message)"
    Clear-ServiceStop
    Start-HeroesReplayStack
    exit 1
}

Protect-MinReplayId (Join-Path $InstallDir 'heroesreplay.exe') $failedSettings (Join-Path $InstallDir 'appsettings.json')
Remove-Item -LiteralPath $failedSettings -Force -ErrorAction SilentlyContinue
Invoke-ReleaseCommand (Join-Path $InstallDir 'heroesreplay.exe') @('update', 'install-obs', '--install', $InstallDir, '--environment', $environment) 'OBS scene files'

Clear-ServiceStop
try {
    Start-HeroesReplayStack
}
catch {
    Write-Host "Could not start HeroesReplay after the rollback: $($_.Exception.Message)"
    exit 1
}

Write-Host "Rolled back to $(Read-Version $InstallDir) from $previous. Release $Version is skipped until it is removed from $skipList."
exit 1
