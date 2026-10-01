# Makes sure a release install has appsettings.secrets.json beside heroesreplay.exe
# and Data\client_secrets.json for YouTube. Ships in the release zip next to the exe.
# Order: the install's own file, then the C:\heroesreplay\secrets backup, then
# 1Password (fill-secrets-from-op.ps1, needs `op` and user env OP_SERVICE_ACCOUNT).
# The newer of the install file and the backup wins, because every release update
# copies the backup over the install.
# The Twitch token is checked with Twitch, and Twitch:GrantedScopes is filled from it,
# because `services start` refuses the Twitch role without those scopes.
# Prints key names, lengths, and scopes only. Never prints a secret value.
# Exit 0 when the install has every required secret and a valid Twitch token. Exit 1 otherwise.
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$SecretsDir = 'C:\heroesreplay\secrets',
    [string]$DataDir = 'C:\heroesreplay\Data',
    [switch]$NoOnePassword,
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'

$required = @(
    'Twitch.AccessToken',
    'Twitch.RefreshToken',
    'Twitch.ClientId',
    'HeroesProfileApi.ApiKey',
    'YouTube.ApiKey'
)

# One of each group must be granted (ServiceRoleChecks.ScopesCover).
$scopeGroups = @(
    @('chat:edit', 'user:write:chat'),
    @('channel:read:redemptions', 'channel:manage:redemptions'),
    @('channel:manage:predictions')
)

$target = Join-Path $InstallDir 'appsettings.secrets.json'
$backup = Join-Path $SecretsDir 'appsettings.secrets.json'

function Read-Json([string]$Path) {
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-Value($Json, [string]$Key) {
    $value = $Json
    foreach ($part in $Key.Split('.')) {
        if ($null -eq $value) {
            return $null
        }

        $value = $value.$part
    }

    return [string]$value
}

function Get-MissingSecrets([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $required
    }

    $json = Read-Json $Path
    if ($null -eq $json) {
        return $required
    }

    $missing = @()
    foreach ($key in $required) {
        $value = Get-Value $json $key
        if ([string]::IsNullOrWhiteSpace($value)) {
            $missing += $key
        }
        elseif ($value -like 'op://*' -and -not (Get-Command op -ErrorAction SilentlyContinue)) {
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

$targetOk = @(Get-MissingSecrets $target).Count -eq 0
$backupOk = @(Get-MissingSecrets $backup).Count -eq 0
if ($targetOk) {
    Write-Host "Install secrets OK: $target"
}
elseif ($backupOk) {
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

$json = Read-Json $target
$failed = $false
if (-not $Offline) {
    $token = Get-Value $json 'Twitch.AccessToken'
    if ($token -like 'op://*') {
        Write-Host 'Twitch token is an op:// reference. Skipping the token check.'
    }
    else {
        try {
            $validate = Invoke-RestMethod -Uri 'https://id.twitch.tv/oauth2/validate' -Headers @{ Authorization = "OAuth $token" }
            $scopes = @($validate.scopes)
            $expires = if ([int]$validate.expires_in -le 0) { 'does not expire' } else { "expires in $([math]::Round($validate.expires_in / 60)) min" }
            Write-Host "Twitch token valid for $($validate.login), $expires."
            $granted = (Get-Value $json 'Twitch.GrantedScopes')
            $wanted = ($scopes -join ' ')
            if ($granted -ne $wanted) {
                if ($null -eq $json.Twitch.PSObject.Properties['GrantedScopes']) {
                    $json.Twitch | Add-Member -NotePropertyName GrantedScopes -NotePropertyValue $wanted
                }
                else {
                    $json.Twitch.GrantedScopes = $wanted
                }

                $json | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $target -Encoding utf8
                Write-Host 'Wrote Twitch:GrantedScopes from the token.'
            }

            foreach ($group in $scopeGroups) {
                if (-not ($group | Where-Object { $scopes -contains $_ })) {
                    Write-Host "  Twitch token lacks scope: $($group -join ' or ')"
                    $failed = $true
                }
            }
        }
        catch {
            Write-Host "Twitch token check failed: $($_.Exception.Message)"
            Write-Host 'The access token is expired or revoked. Put a new one in 1Password and the secrets backup.'
            $failed = $true
        }
    }
}

New-Item -ItemType Directory -Force -Path $SecretsDir | Out-Null
$backupOk = @(Get-MissingSecrets $backup).Count -eq 0
$differs = -not $backupOk -or ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $backup).Hash)
if ($differs) {
    if (-not $backupOk -or (Get-Item -LiteralPath $target).LastWriteTimeUtc -ge (Get-Item -LiteralPath $backup).LastWriteTimeUtc) {
        Copy-Item -LiteralPath $target -Destination $backup -Force
        Write-Host "Updated the backup $backup from the install."
    }
    else {
        Write-Host "WARNING: $backup is newer than the install copy and differs. The next update will install the backup."
    }
}

$json = Read-Json $target
foreach ($key in $required) {
    Write-Host ("  {0} len={1}" -f $key, (Get-Value $json $key).Length)
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
        # services start refuses the YouTube role in prod without it.
        Write-Host "MISSING: $clientSecrets. The YouTube role will not start. Restore it from 1Password (fill-secrets-from-op.ps1)."
        $failed = $true
    }
}
elseif (-not (Test-Path -LiteralPath $clientBackup)) {
    Copy-Item -LiteralPath $clientSecrets -Destination $clientBackup -Force
    Write-Host "Backed up $clientSecrets to $clientBackup"
}

if ($failed) {
    exit 1
}

exit 0
