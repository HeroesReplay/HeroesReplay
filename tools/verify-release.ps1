# Checks a zip from tools/package-release.ps1 before it can reach the stream PC. ci.yml runs it
# on every pull request and push, and release.yml runs it before the upload.
# Extracts the zip the way the update does (ZipFile.ExtractToDirectory) into a temp folder, runs
# the packaged heroesreplay.exe --help (exit 0), requires what ReleaseInstall, apply-release.ps1,
# and update install-obs read plus every obs/bundle.manifest asset, and fails on service.json,
# appsettings.secrets.json, client_secrets.json, or any token-like file.
# -Version also requires version.txt to equal it: the update compares that file to the tag.
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$root = Split-Path -Parent $PSScriptRoot
$zip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ZipPath)
if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
    throw "No release zip at $zip"
}

$temp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$extract = Join-Path $temp ('heroesreplay-verify-' + [System.IO.Path]::GetRandomFileName())
$failures = [System.Collections.Generic.List[string]]::new()

# ReleaseInstall (heroesreplay.exe at the root, version.txt), the memory match clock and screen
# state (HeroesClientSDK.dll, the package restored from .packages), ReleaseUpdateGate and
# apply-release.ps1 (apply-release.ps1, appsettings.json for MinReplayId), the prod overlay,
# client configure (AhliObs), update install-obs (Default.json, Default\basic.ini), and the MCP
# discovery file (.mcp.json, which starts the read-only `heroesreplay mcp`).
$required = [System.Collections.Generic.List[string]]::new()
@(
    'heroesreplay.exe',
    'heroesreplay.dll',
    'heroesreplay.runtimeconfig.json',
    'HeroesClientSDK.dll',
    'appsettings.json',
    'appsettings.prod.json',
    'apply-release.ps1',
    'version.txt',
    'Assets\Interfaces\AhliObs 0.75.StormInterface',
    'obs\Default.json',
    'obs\Default\basic.ini',
    '.mcp.json'
) | ForEach-Object { $required.Add($_) }

$manifest = @(
    Get-Content -LiteralPath (Join-Path $root 'obs\bundle.manifest') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') }
)
if ($manifest.Count -eq 0) {
    $failures.Add('obs/bundle.manifest lists no assets.')
}
foreach ($asset in $manifest) {
    $required.Add('obs\' + ($asset -replace '/', '\'))
}

# Exact names, then extensions, then a name heuristic. Assemblies and their symbols are skipped
# by the heuristic: Microsoft.Extensions.Configuration.UserSecrets.dll is code, not a secret.
$forbiddenNames = @('service.json', 'appsettings.secrets.json', 'client_secrets.json', 'stream-armed', '.env')
$forbiddenExtensions = @('.pfx', '.p12', '.pem', '.key', '.token', '.snk')
$codeExtensions = @('.dll', '.exe', '.pdb', '.xml')
$tokenLike = 'token|secret|credential|password'

$fileCount = 0
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)

    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $extract $file) -PathType Leaf)) {
            $failures.Add("Missing $file")
        }
    }

    # MCP discovery for an agent started in the install folder must start the read-only server.
    $mcp = Join-Path $extract '.mcp.json'
    if (Test-Path -LiteralPath $mcp -PathType Leaf) {
        try {
            $server = (Get-Content -LiteralPath $mcp -Raw | ConvertFrom-Json).mcpServers.heroesreplay
            if (-not $server -or (@($server.args) -join ' ') -ne 'mcp' -or $server.command -notmatch 'heroesreplay\.exe$') {
                $failures.Add('.mcp.json does not start heroesreplay.exe mcp.')
            }
        }
        catch {
            $failures.Add(".mcp.json is not valid JSON. $($_.Exception.Message)")
        }
    }

    $versionFile = Join-Path $extract 'version.txt'
    if (Test-Path -LiteralPath $versionFile -PathType Leaf) {
        $packaged = [System.IO.File]::ReadAllText($versionFile)
        if ([string]::IsNullOrWhiteSpace($packaged)) {
            $failures.Add('version.txt is empty.')
        }
        elseif ($Version -and $packaged -cne $Version) {
            $failures.Add("version.txt is '$packaged', expected '$Version'.")
        }
    }

    foreach ($file in Get-ChildItem -LiteralPath $extract -Recurse -File -Force) {
        $fileCount++
        $relative = [System.IO.Path]::GetRelativePath($extract, $file.FullName)
        if ($forbiddenNames -contains $file.Name -or $forbiddenExtensions -contains $file.Extension) {
            $failures.Add("Secret file in the zip: $relative")
        }
        elseif ($codeExtensions -notcontains $file.Extension -and $file.Name -match $tokenLike) {
            $failures.Add("Token-like file in the zip: $relative")
        }
    }

    $exe = Join-Path $extract 'heroesreplay.exe'
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        $start = [System.Diagnostics.ProcessStartInfo]::new($exe, '--help')
        $start.WorkingDirectory = $extract
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $help = [System.Diagnostics.Process]::Start($start)
        $stdout = $help.StandardOutput.ReadToEndAsync()
        $stderr = $help.StandardError.ReadToEndAsync()
        if (-not $help.WaitForExit(120000)) {
            $help.Kill($true)
            $failures.Add('heroesreplay.exe --help did not exit within 2 minutes.')
        }
        elseif ($help.ExitCode -ne 0) {
            $failures.Add("heroesreplay.exe --help exited $($help.ExitCode): $($stderr.Result.Trim())")
        }
        elseif ($stdout.Result -notmatch 'spectate') {
            $failures.Add('heroesreplay.exe --help did not list the spectate command.')
        }
    }
}
finally {
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        if ($env:GITHUB_ACTIONS -eq 'true') {
            Write-Host "::error::$failure"
        }
        else {
            Write-Host $failure
        }
    }

    Write-Host "Release zip check failed: $($failures.Count) problem(s) in $zip"
    exit 1
}

Write-Host "Release zip ok: $zip ($fileCount files, $($manifest.Count) OBS bundle assets, version.txt '$packaged', --help exit 0)."
exit 0
