# Fills gitignored src/HeroesReplay.CLI/appsettings.secrets.json from the
# 1Password service account. Requires user env OP_SERVICE_ACCOUNT (ops_...).
# Does not print secret values. See .grok/skills/op-service-account/SKILL.md.
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

if (Test-Path $dest) {
    $j = Get-Content $dest -Raw | ConvertFrom-Json
} else {
    $j = Get-Content $example -Raw | ConvertFrom-Json
}

function Read-Op([string]$uri) {
    $v = (op read $uri).Trim()
    if ([string]::IsNullOrWhiteSpace($v)) {
        throw "Empty secret for $uri"
    }
    return $v
}

$j.HeroesProfileApi.ApiKey = Read-Op 'op://Heroes Replay/Heroes Profile API Key/password'
$j.Twitch.AccessToken = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Access Token'
$j.Twitch.RefreshToken = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Refresh Token'
$j.Twitch.ClientId = Read-Op 'op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Client Id'
$j.Twitch.Account = 'saltysadism'
$j.Twitch.Channel = 'SaltySadism'

$j | ConvertTo-Json -Depth 8 | Set-Content $dest -Encoding utf8

$bins = Get-ChildItem (Join-Path $cli 'bin') -Recurse -Filter appsettings.json -ErrorAction SilentlyContinue
foreach ($app in $bins) {
    Copy-Item -Force $dest (Join-Path $app.DirectoryName 'appsettings.secrets.json')
}

Write-Host "wrote $dest"
Write-Host ("HeroesProfileApi.ApiKey len={0} Twitch.AccessToken len={1} ClientId len={2}" -f `
    $j.HeroesProfileApi.ApiKey.Length, $j.Twitch.AccessToken.Length, $j.Twitch.ClientId.Length)
