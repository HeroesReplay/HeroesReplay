# Starts HeroesReplay when the streaming user logs on, and gives apply-release.ps1 the
# launcher it looks for first. Ships in the release zip next to heroesreplay.exe.
# Run once, elevated, as the Windows user that is signed in to Battle.net and OBS.
# Writes %LOCALAPPDATA%\HeroesReplay\start-live.cmd and the scheduled task HeroesReplay-live:
# at logon of this user, interactive (the game, OBS, and capture need the desktop), highest privileges
# (spectate requires administrator). -Remove deletes both.
param(
    [string]$InstallDir = $PSScriptRoot,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$taskName = 'HeroesReplay-live'
$launcher = Join-Path $env:LOCALAPPDATA 'HeroesReplay\start-live.cmd'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $launcher -Force -ErrorAction SilentlyContinue
    Write-Host "Removed $taskName and $launcher."
    exit 0
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Run this from an elevated PowerShell.'
    exit 1
}

$exe = Join-Path $InstallDir 'heroesreplay.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "No heroesreplay.exe in $InstallDir."
    exit 1
}

New-Item -ItemType Directory -Force -Path (Split-Path $launcher) | Out-Null
Set-Content -LiteralPath $launcher -Encoding ascii -Value @(
    '@echo off',
    'set HEROES_REPLAY_ENV=prod',
    "cd /d `"$InstallDir`"",
    'heroesreplay.exe services start'
)

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"$launcher`"" -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
# Give Explorer, Battle.net, and the network a moment after logon.
$trigger.Delay = 'PT1M'
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null

Write-Host "Wrote $launcher"
Write-Host "Registered $taskName for $user (at logon, interactive, highest privileges)."
Write-Host "Start it now with: schtasks /Run /TN $taskName"
