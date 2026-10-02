# Builds heroesreplay-win-x64.zip, the release production installs. release.yml uploads it;
# ci.yml builds it on every pull request and push, and tools/verify-release.ps1 checks it.
# Framework-dependent win-x64 publish. The CLI project publishes obs/bundle.manifest under obs\.
# The zip never carries appsettings.secrets.json or OBS service.json (the stream key).
# version.txt is $Version exactly, with no newline: the update compares it to the release tag.
param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Version,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$root = Split-Path -Parent $PSScriptRoot
$zip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$out = Join-Path $temp ('heroesreplay-publish-' + [System.IO.Path]::GetRandomFileName())

Push-Location $root
try {
    dotnet publish src/HeroesReplay.CLI/HeroesReplay.CLI.csproj -c $Configuration -r win-x64 --self-contained false -o $out --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish exited $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $out 'obs\Default') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'obs\Default.json') -Destination (Join-Path $out 'obs\Default.json') -Force
    Copy-Item -LiteralPath (Join-Path $root 'obs\Default\basic.ini') -Destination (Join-Path $out 'obs\Default\basic.ini') -Force
    Remove-Item -LiteralPath (Join-Path $out 'appsettings.secrets.json') -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $out -Recurse -Force -Filter service.json | Remove-Item -Force
    Set-Content -LiteralPath (Join-Path $out 'version.txt') -Value $Version -NoNewline

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $zip) | Out-Null
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host "Packaged $Version ($Configuration) -> $zip"
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
}
