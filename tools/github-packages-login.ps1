# Lets this machine restore HeroesClientSDK (the memory match clock and screen state) from GitHub
# Packages: the `github` source in nuget.config. GitHub Packages needs a token with read:packages
# for a NuGet restore, even for a public package.
# The credential goes into the user-level NuGet config (%APPDATA%\NuGet\NuGet.Config) as
# packageSourceCredentials for the `github` key only. No source is added there, so other repos on
# this machine do not see GitHub Packages, and the repo's nuget.config never holds a token.
# Token: -Token, else -OpReference (a 1Password read:packages PAT, skill op-service-account),
# else `gh auth token`. A gh login without that scope needs, once:
#   gh auth refresh -h github.com -s read:packages
# tools/bootstrap-workstation.ps1 runs this. CI authenticates with GITHUB_TOKEN instead.
param(
    [string]$Token,
    [string]$UserName,
    [string]$OpReference
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$sourceKey = 'github'
$probe = 'https://nuget.pkg.github.com/HeroesReplay/download/heroesclientsdk/index.json'

if (-not $Token -and $OpReference) {
    if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
        $env:OP_SERVICE_ACCOUNT_TOKEN = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT', 'User')
    }

    $Token = (op read $OpReference)
    if ($LASTEXITCODE -ne 0 -or -not $Token) {
        throw "op read $OpReference failed."
    }
}

if (-not $Token -and (Get-Command gh -ErrorAction SilentlyContinue)) {
    $Token = (gh auth token 2>$null)
    if (-not $UserName) {
        $UserName = (gh api user --jq .login 2>$null)
    }
}

if (-not $Token) {
    throw 'No token for GitHub Packages. Log in with gh (gh auth login -s read:packages), or pass -Token or -OpReference.'
}

$Token = $Token.Trim()
if (-not $UserName) {
    # GitHub Packages checks the token, not the user name.
    $UserName = 'HeroesReplay'
}

$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${UserName}:$Token"))
try {
    Invoke-WebRequest -Uri $probe -Headers @{ Authorization = "Basic $basic" } -UseBasicParsing | Out-Null
}
catch {
    $status = $_.Exception.Response.StatusCode.value__
    throw "GitHub Packages refused the token (HTTP $status) for HeroesClientSDK. The token needs read:packages: gh auth refresh -h github.com -s read:packages"
}

$config = Join-Path $env:APPDATA 'NuGet\NuGet.Config'
if (-not (Test-Path -LiteralPath $config)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $config) | Out-Null
    Set-Content -LiteralPath $config -Value '<?xml version="1.0" encoding="utf-8"?><configuration></configuration>' -Encoding utf8
}

$xml = [xml](Get-Content -LiteralPath $config -Raw)
$root = $xml.SelectSingleNode('/configuration')
$credentials = $root.SelectSingleNode('packageSourceCredentials')
if (-not $credentials) {
    $credentials = $root.AppendChild($xml.CreateElement('packageSourceCredentials'))
}

$existing = $credentials.SelectSingleNode($sourceKey)
if ($existing) {
    $credentials.RemoveChild($existing) | Out-Null
}

$entry = $credentials.AppendChild($xml.CreateElement($sourceKey))
foreach ($pair in @(@('Username', $UserName), @('ClearTextPassword', $Token))) {
    $add = $entry.AppendChild($xml.CreateElement('add'))
    $add.SetAttribute('key', $pair[0])
    $add.SetAttribute('value', $pair[1])
}

$xml.Save($config)
$Token = $null
Write-Host "GitHub Packages: the '$sourceKey' source credential is in $config. HeroesClientSDK restores."
