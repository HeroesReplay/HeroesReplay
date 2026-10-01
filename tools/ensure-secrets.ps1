# Makes sure a release install has appsettings.secrets.json beside heroesreplay.exe
# and Data\client_secrets.json for YouTube. Ships in the release zip next to the exe.
# Order: the install's own file, then the C:\heroesreplay\secrets backup, then
# 1Password (fill-secrets-from-op.ps1, needs `op` and user env OP_SERVICE_ACCOUNT).
# A good install file is copied back to the backup so the next update keeps it.
# Prints key names and lengths only. Never prints a value.
# Exit 0 when the install has every required secret. Exit 1 otherwise.
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$SecretsDir = 'C:\heroesreplay\secrets',
    [string]$DataDir = 'C:\heroesreplay\Data',
    [switch]$NoOnePassword
)

$ErrorActionPreference = 'Stop'

$required = @(
    'Twitch.AccessToken',
    'Twitch.RefreshToken',
    'Twitch.ClientId',
    'HeroesProfileApi.ApiKey',
    'YouTube.ApiKey'
)

$target = Join-Path $InstallDir 'appsettings.secrets.json'
$backup = Join-Path $SecretsDir 'appsettings.secrets.json'

function Get-MissingSecrets([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $required
    }

    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $required
    }

    $missing = @()
    foreach ($key in $required) {
        $value = $json
        foreach ($part in $key.Split('.')) {
            if ($null -eq $value) {
                break
            }

            $value = $value.$part
        }

        if ([string]::IsNullOrWhiteSpace([string]$value)) {
            $missing += $key
        }
        elseif ([string]$value -like 'op://*' -and -not (Get-Command op -ErrorAction SilentlyContinue)) {
            # heroesreplay resolves op:// at startup. Without the op CLI that value is empty.
            $missing += "$key (op:// reference, op CLI not installed)"
        }
    }

    return $missing
}

function Test-OnePassword {
    if (-not (Get-Command op -ErrorAction SilentlyContinue)) {
        return $false
    }

    return [bool]($env:OP_SERVICE_ACCOUNT_TOKEN `
        -or $env:OP_SERVICE_ACCOUNT `
        -or [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT', 'User'))
}

$missing = @(Get-MissingSecrets $target)
if ($missing.Count -eq 0) {
    Write-Host "Install secrets OK: $target"
}
elseif (@(Get-MissingSecrets $backup).Count -eq 0) {
    Copy-Item -LiteralPath $backup -Destination $target -Force
    Write-Host "Restored $target from $backup"
}
elseif (-not $NoOnePassword -and (Test-OnePassword)) {
    $fill = Join-Path $PSScriptRoot 'fill-secrets-from-op.ps1'
    if (-not (Test-Path -LiteralPath $fill)) {
        Write-Host "Missing $fill. Cannot read 1Password."
        exit 1
    }

    Write-Host 'Install and backup secrets are incomplete. Filling from 1Password.'
    & $fill -Destination $target
}

$missing = @(Get-MissingSecrets $target)
if ($missing.Count -gt 0) {
    Write-Host "appsettings.secrets.json is incomplete at $target"
    foreach ($key in $missing) {
        Write-Host "  missing: $key"
    }

    Write-Host "Put a complete file at $backup, or set user env OP_SERVICE_ACCOUNT and install the 1Password CLI (op), then run this script again."
    exit 1
}

New-Item -ItemType Directory -Force -Path $SecretsDir | Out-Null
if (@(Get-MissingSecrets $backup).Count -gt 0) {
    Copy-Item -LiteralPath $target -Destination $backup -Force
    Write-Host "Backed up install secrets to $backup"
}

$json = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json
foreach ($key in $required) {
    $value = $json
    foreach ($part in $key.Split('.')) {
        $value = $value.$part
    }

    Write-Host ("  {0} len={1}" -f $key, ([string]$value).Length)
}

$clientSecrets = Join-Path $DataDir 'client_secrets.json'
$clientBackup = Join-Path $SecretsDir 'client_secrets.json'
if (-not (Test-Path -LiteralPath $clientSecrets)) {
    if (Test-Path -LiteralPath $clientBackup) {
        New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
        Copy-Item -LiteralPath $clientBackup -Destination $clientSecrets -Force
        Write-Host "Restored $clientSecrets from $clientBackup"
    }
    else {
        # Not fatal: spectate, Twitch, and downloads still run. Only real YouTube uploads need it.
        Write-Host "WARNING: $clientSecrets is missing. YouTube uploads will fail until it exists."
    }
}
elseif (-not (Test-Path -LiteralPath $clientBackup)) {
    Copy-Item -LiteralPath $clientSecrets -Destination $clientBackup -Force
    Write-Host "Backed up $clientSecrets to $clientBackup"
}

exit 0
