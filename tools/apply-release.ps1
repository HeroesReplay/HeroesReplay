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

# The spectator starts this script hidden. Keep a log so a failed update can be read afterwards.
$logDir = Join-Path $env:LOCALAPPDATA 'HeroesReplay\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
try {
    Start-Transcript -Path (Join-Path $logDir 'apply-release.log') -Append | Out-Null
}
catch {
}

if ($WaitForPid -gt 0) {
    Wait-Process -Id $WaitForPid -ErrorAction SilentlyContinue
}

# An agent's `heroesreplay mcp` server only reads status. It holds the install's files open
# and never exits on services.stop, so close it here. The agent's client starts it again.
Get-CimInstance Win32_Process -Filter "Name='heroesreplay.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match '\smcp(\s|$)' } |
    ForEach-Object {
        Write-Host "Stopping heroesreplay mcp pid $($_.ProcessId) so the install can be replaced."
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
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

$previous = "$InstallDir.previous"
if (Test-Path -LiteralPath $previous) {
    $roleFile = Join-Path $InstallDir 'role-ready.txt'
    $healthExe = Join-Path $source 'heroesreplay.exe'
    & $healthExe update release-health --role-file $roleFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Previous install is still inside the stabilization window. Restarting the current build."
        Clear-ServiceStop
        Start-HeroesReplayStack
        exit 1
    }

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
    Write-Host "Previous install kept at $previous until the new stack has been healthy."
}
