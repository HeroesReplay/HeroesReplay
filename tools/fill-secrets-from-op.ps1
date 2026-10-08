# Fills gitignored src/HeroesReplay.CLI/appsettings.secrets.json from the
# 1Password service account. Requires user env OP_SERVICE_ACCOUNT (ops_...).
# Does not print secret values. See .agents/skills/op-service-account/SKILL.md.
$ErrorActionPreference = 'Stop'

if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $t = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT', 'User')
    if (-not $t) {
        $t = $env:OP_SERVICE_ACCOUNT
    }
    if ($t) {
        $env:OP_SERVICE_ACCOUNT_TOKEN = $t
    }
}

if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    throw 'Set user environment variable OP_SERVICE_ACCOUNT to the 1Password service-account token (ops_...).'
}

$who = (op whoami | Out-String)
if ($who -notmatch 'SERVICE_ACCOUNT') {
    throw "op whoami is not SERVICE_ACCOUNT. Check OP_SERVICE_ACCOUNT. Output:$who"
}

$root = git rev-parse --show-toplevel
if (-not $root) {
    throw 'Run from inside the HeroesReplay git clone.'
}

$cli = Join-Path $root 'src/HeroesReplay.CLI'
$dest = Join-Path $cli 'appsettings.secrets.json'
$example = Join-Path $cli 'appsettings.secrets.example.json'
if (-not (Test-Path $example)) {
    throw "Missing $example"
}

function Read-Op([string]$uri) {
    $v = (op read $uri).Trim()
    if ([string]::IsNullOrWhiteSpace($v)) {
        throw "Empty secret for $uri"
    }
    return $v
}

# Canonical shape only. Do not round-trip leftover keys (AWS, the old form-API
# extension fields, GitHub).
# YouTube Uploader item uuid xrstilaqn2jygtuwwde346ozwm
$j = [ordered]@{
    Twitch = [ordered]@{
        Account      = 'saltysadism'
        AccessToken  = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Access Token'
        ClientId     = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Client Id'
        RefreshToken = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Refresh Token'
        Channel      = 'SaltySadism'
    }
    HeroesProfileApi = [ordered]@{
        ApiKey = Read-Op 'op://Heroes Replay/Heroes Profile API Key/password'
    }
    YouTube = [ordered]@{
        ApiKey = Read-Op 'op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/API Key'
    }
}

# Heroes Profile Twitch extension uploader key (issue 49), sent as X-HP-Twitch-Key.
# It is not the v1 Bearer key. Optional: written only once the item exists, so the
# other secrets still fill while it is missing. TwitchExtension:Enabled stays false
# until `heroesreplay check twitch-extension` reports entitlement.active=True.
$uploaderItem = 'Heroes Profile Twitch Uploader Key'
$items = op item list --vault 'Heroes Replay' --format json | ConvertFrom-Json
if (@($items | Where-Object { $_.title -eq $uploaderItem }).Count -gt 0) {
    $j.TwitchExtension = [ordered]@{
        ApiKey = Read-Op "op://Heroes Replay/$uploaderItem/password"
    }
}
else {
    Write-Warning "1Password item '$uploaderItem' is missing; TwitchExtension:ApiKey is not written."
}

$j | ConvertTo-Json -Depth 8 | Set-Content $dest -Encoding utf8

$bins = Get-ChildItem (Join-Path $cli 'bin') -Recurse -Filter appsettings.json -ErrorAction SilentlyContinue
foreach ($app in $bins) {
    Copy-Item -Force $dest (Join-Path $app.DirectoryName 'appsettings.secrets.json')
}

$ytClientId = Read-Op 'op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Client ID'
$ytClientSecret = Read-Op 'op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Client Secret'
$ytProjectId = Read-Op 'op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Project ID'
$clientSecrets = [ordered]@{
    installed = [ordered]@{
        client_id                   = $ytClientId
        project_id                  = $ytProjectId
        auth_uri                    = 'https://accounts.google.com/o/oauth2/auth'
        token_uri                   = 'https://oauth2.googleapis.com/token'
        auth_provider_x509_cert_url = 'https://www.googleapis.com/oauth2/v1/certs'
        client_secret               = $ytClientSecret
        redirect_uris               = @('http://localhost')
    }
}
$dataDir = 'C:\heroesreplay\Data'
$secretsDir = 'C:\heroesreplay\secrets'
New-Item -ItemType Directory -Force -Path $dataDir, $secretsDir | Out-Null
$clientSecretsPath = Join-Path $dataDir 'client_secrets.json'
$clientSecrets | ConvertTo-Json -Depth 8 | Set-Content $clientSecretsPath -Encoding utf8
Copy-Item -Force $clientSecretsPath (Join-Path $secretsDir 'client_secrets.json')
Copy-Item -Force $dest (Join-Path $secretsDir 'appsettings.secrets.json')

Write-Host "wrote $dest"
Write-Host ("HeroesProfileApi.ApiKey len={0} Twitch.AccessToken len={1} ClientId len={2} YouTube.ApiKey len={3} client_secrets client_id len={4} TwitchExtension.ApiKey len={5}" -f `
    $j.HeroesProfileApi.ApiKey.Length, $j.Twitch.AccessToken.Length, $j.Twitch.ClientId.Length, $j.YouTube.ApiKey.Length, $ytClientId.Length, $(if ($j.Contains('TwitchExtension')) { $j.TwitchExtension.ApiKey.Length } else { 0 }))
