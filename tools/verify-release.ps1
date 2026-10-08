# Checks a zip from tools/package-release.ps1 before it can reach the stream PC. ci.yml runs it
# on every pull request and push, and release.yml runs it before the upload.
# Extracts the zip the way the update does (ZipFile.ExtractToDirectory) into a temp folder, runs
# the packaged heroesreplay.exe --help (exit 0), requires what ReleaseInstall, apply-release.ps1,
# and update install-obs read plus every obs/bundle.manifest asset, checks the zip's schema 2
# obs\bundle.manifest (each size and SHA-256, Default.json's hash, the scene and source contract)
# in PowerShell and with the packaged `obs bundle`, and fails on service.json,
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

# The checkout's obs\bundle.manifest is the asset list the build publishes. The zip's
# obs\bundle.manifest is schema 2 (package-release.ps1): it must list every one of those assets.
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
$required.Add('obs\bundle.manifest')

# The zip's obs\bundle.manifest (schema 2) against the extracted bytes: every listed asset is in
# it, each file's size and SHA-256, Default.json's hash, and the scene and source contract
# against the packaged Default.json. One line per problem.
function Test-ObsBundle([string]$Obs, [string[]]$Listed) {
    $problems = [System.Collections.Generic.List[string]]::new()
    try {
        $bundle = Get-Content -LiteralPath (Join-Path $Obs 'bundle.manifest') -Raw | ConvertFrom-Json
    }
    catch {
        $problems.Add("obs/bundle.manifest is not the schema 2 JSON manifest. $($_.Exception.Message)")
        return $problems
    }

    if ($bundle.schemaVersion -ne 2) {
        $problems.Add("obs/bundle.manifest has schemaVersion '$($bundle.schemaVersion)', expected 2.")
        return $problems
    }

    $assets = @($bundle.assets)
    $paths = @($assets | ForEach-Object { $_.path })
    foreach ($asset in $Listed) {
        if ($paths -notcontains $asset) {
            $problems.Add("obs/bundle.manifest does not list obs/$asset.")
        }
    }

    foreach ($asset in $assets) {
        $file = Join-Path $Obs ($asset.path -replace '/', '\')
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            $problems.Add("obs/$($asset.path) is in obs/bundle.manifest but not in the zip.")
            continue
        }

        $length = (Get-Item -LiteralPath $file).Length
        if ($length -ne [long]$asset.size) {
            $problems.Add("obs/$($asset.path) is $length bytes, but obs/bundle.manifest says $($asset.size).")
            continue
        }

        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ($hash -ne $asset.sha256) {
            $problems.Add("obs/$($asset.path) has SHA-256 $hash, but obs/bundle.manifest says $($asset.sha256).")
        }
    }

    $collection = Join-Path $Obs 'Default.json'
    if (-not (Test-Path -LiteralPath $collection -PathType Leaf)) {
        return $problems
    }

    if ((Get-FileHash -LiteralPath $collection -Algorithm SHA256).Hash -ne $bundle.collectionSha256) {
        $problems.Add("obs/Default.json does not match the manifest's collectionSha256 $($bundle.collectionSha256).")
    }

    $contract = $bundle.contract
    if (-not $contract -or @($contract.scenes).Count -eq 0 -or @($contract.sources).Count -eq 0) {
        $problems.Add('obs/bundle.manifest has no scene and source contract.')
        return $problems
    }

    # Names compare case-sensitively, as OBS and ObsContract do.
    $kinds = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $items = [System.Collections.Generic.Dictionary[string, string[]]]::new([System.StringComparer]::Ordinal)
    foreach ($source in @((Get-Content -LiteralPath $collection -Raw | ConvertFrom-Json).sources)) {
        if (-not $source.name -or $kinds.ContainsKey($source.name)) {
            continue
        }

        $kinds[$source.name] = $source.id
        if ($source.id -ceq 'scene') {
            $items[$source.name] = [string[]]@($source.settings.items | ForEach-Object { $_.name })
        }
    }

    foreach ($scene in @($contract.scenes)) {
        if (-not $items.ContainsKey($scene)) {
            $problems.Add("obs/Default.json has no scene '$scene', which the contract names.")
        }
    }

    foreach ($source in @($contract.sources)) {
        if (-not $kinds.ContainsKey($source.name)) {
            $problems.Add("obs/Default.json has no source '$($source.name)', which the contract names.")
        }
        elseif ($source.kind -and $kinds[$source.name] -cne $source.kind) {
            $problems.Add("obs/Default.json has '$($source.name)' as a $($kinds[$source.name]), but the contract says $($source.kind).")
        }
    }

    foreach ($item in @($contract.items)) {
        if ($items.ContainsKey($item.scene) -and $kinds.ContainsKey($item.source) -and $items[$item.scene] -cnotcontains $item.source) {
            $problems.Add("obs/Default.json has no item '$($item.source)' in scene '$($item.scene)', which the contract names.")
        }
    }

    return $problems
}

# Runs the packaged exe from the extracted folder. ExitCode is -1 when it ran over 2 minutes.
function Invoke-Packaged([string]$Exe, [string[]]$Arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($argument in $Arguments) {
        $start.ArgumentList.Add($argument)
    }

    $start.WorkingDirectory = Split-Path -Parent $Exe
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) {
        $process.Kill($true)
        return [pscustomobject]@{ ExitCode = -1; Output = ''; Error = '' }
    }

    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $stdout.Result; Error = $stderr.Result }
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

    $bundleFile = Join-Path $extract 'obs\bundle.manifest'
    if (Test-Path -LiteralPath $bundleFile -PathType Leaf) {
        foreach ($problem in (Test-ObsBundle (Join-Path $extract 'obs') $manifest)) {
            $failures.Add($problem)
        }
    }

    $exe = Join-Path $extract 'heroesreplay.exe'
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        $help = Invoke-Packaged $exe @('--help')
        if ($help.ExitCode -eq -1) {
            $failures.Add('heroesreplay.exe --help did not exit within 2 minutes.')
        }
        elseif ($help.ExitCode -ne 0) {
            $failures.Add("heroesreplay.exe --help exited $($help.ExitCode): $($help.Error.Trim())")
        }
        elseif ($help.Output -notmatch 'spectate') {
            $failures.Add('heroesreplay.exe --help did not list the spectate command.')
        }

        # The app-side reader that update install-obs and obs validate use, on the same bytes.
        if (Test-Path -LiteralPath $bundleFile -PathType Leaf) {
            $bundle = Invoke-Packaged $exe @('obs', 'bundle', '--install', $extract)
            if ($bundle.ExitCode -ne 0 -or $bundle.Output -notmatch 'schema 2') {
                $failures.Add("heroesreplay.exe obs bundle exited $($bundle.ExitCode): $(($bundle.Output + ' ' + $bundle.Error).Trim())")
            }
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

Write-Host "Release zip ok: $zip ($fileCount files, $($manifest.Count) OBS bundle assets matching obs\bundle.manifest SHA-256 and contract, version.txt '$packaged', --help exit 0)."
exit 0
