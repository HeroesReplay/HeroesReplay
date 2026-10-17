# Builds heroesreplay-win-x64.zip, the release production installs. release.yml uploads it;
# ci.yml builds it on every pull request and push, and tools/verify-release.ps1 checks it.
# Framework-dependent win-x64 publish. The CLI project publishes the assets obs/bundle.manifest
# lists under obs\, and the published build writes the zip's obs\bundle.manifest (schema 2).
# The zip never carries appsettings.secrets.json or OBS service.json (the stream key).
# version.txt is $Version exactly, with no newline: the update compares it to the release tag.
# runtime.txt is the .NET runtime the packaged exe runs on (#462), from its `check runtime`.
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

    # runtime.txt (#462): the .NET runtime the packaged exe runs on here, as `check runtime` reports
    # it (RuntimeInformation.FrameworkDescription, such as ".NET 10.0.13"), with no newline.
    # release.yml names it in the release notes, and the runtime-currency workflow compares it
    # with the newest .NET security release. version.txt stays the tag alone.
    $check = & (Join-Path $out 'heroesreplay.exe') check runtime --output json
    if ($LASTEXITCODE -ne 0) {
        throw "check runtime exited $LASTEXITCODE"
    }

    $detail = "$((($check -join "`n") | ConvertFrom-Json).details.checks[0].detail)"
    if ($detail -notmatch '^(?<runtime>\.NET \d+\.\d+\.\d+\S*?)(?:,|\.?(?:\s|$))') {
        throw "check runtime did not start with a .NET runtime: $detail"
    }

    $runtime = $Matches.runtime
    Set-Content -LiteralPath (Join-Path $out 'runtime.txt') -Value $runtime -NoNewline
    # MCP discovery for an agent started in the install folder: the read-only `heroesreplay mcp`.
    Copy-Item -LiteralPath (Join-Path $root 'tools\release.mcp.json') -Destination (Join-Path $out '.mcp.json') -Force

    # obs\bundle.manifest, schema 2 (#308): the published build turns the checkout's asset list into
    # JSON with each packaged OBS file's size and SHA-256, Default.json's hash, and the scene and
    # source contract (ObsContract from the packaged appsettings.json and prod overlay). The hashes
    # are of the files in this folder, so they are the bytes the zip carries. Last, after every OBS
    # file is in place. update install-obs refuses a bundle that no longer matches it.
    Copy-Item -LiteralPath (Join-Path $root 'obs\bundle.manifest') -Destination (Join-Path $out 'obs\bundle.manifest') -Force
    & (Join-Path $out 'heroesreplay.exe') obs bundle --install $out --write
    if ($LASTEXITCODE -ne 0) {
        throw "obs bundle --write exited $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $zip) | Out-Null
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host "Packaged $Version ($Configuration, $runtime) -> $zip"
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
}
