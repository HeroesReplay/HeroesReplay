# Puts HeroesClientSDK.<version>.nupkg into .packages, the local source nuget.config maps
# HeroesClientSDK to. The version and its SHA-256 are pinned together in Directory.Packages.props
# (HeroesClientSDKVersion, HeroesClientSDKSha256). The file is the public GitHub Release asset of
# HeroesReplay/HeroesClientSDK, downloaded without credentials, and it is used only when its
# SHA-256 matches the pin. Idempotent: a file that is already there with the pinned hash is kept.
# Runs in ci.yml and release.yml before anything restores, from bootstrap-workstation.ps1, and
# from the build (Directory.Build.targets) when the file is missing.
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$props = Join-Path $Root 'Directory.Packages.props'
$xml = [xml](Get-Content -LiteralPath $props -Raw)
$version = "$($xml.Project.PropertyGroup.HeroesClientSDKVersion)".Trim()
$sha256 = "$($xml.Project.PropertyGroup.HeroesClientSDKSha256)".Trim().ToLowerInvariant()
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "HeroesClientSDKVersion '$version' in $props is not a version."
}

if ($sha256 -notmatch '^[0-9a-f]{64}$') {
    throw "HeroesClientSDKSha256 in $props is not a SHA-256."
}

$name = "HeroesClientSDK.$version.nupkg"
$folder = Join-Path $Root '.packages'
$target = Join-Path $folder $name
$url = "https://github.com/HeroesReplay/HeroesClientSDK/releases/download/v$version/$name"

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-Pinned {
    (Test-Path -LiteralPath $target -PathType Leaf) -and (Get-Sha256 $target) -eq $sha256
}

if (Test-Pinned) {
    Write-Host "HeroesClientSDK $version is in .packages (SHA-256 ok)."
    exit 0
}

New-Item -ItemType Directory -Force -Path $folder | Out-Null

# The build runs this for each project in parallel. One download at a time per checkout; the
# others find the file once they hold the lock.
$key = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($folder.ToLowerInvariant()))
).Replace('-', '').Substring(0, 16)
$lock = [System.Threading.Mutex]::new($false, "Local\HeroesClientSDK-restore-$key")
$held = $false
try {
    try {
        $held = $lock.WaitOne([TimeSpan]::FromMinutes(5))
    }
    catch [System.Threading.AbandonedMutexException] {
        $held = $true
    }

    if (Test-Pinned) {
        Write-Host "HeroesClientSDK $version is in .packages (SHA-256 ok)."
        exit 0
    }

    # A unique temp name in the same folder, then a rename: NuGet never sees half a file.
    $temp = Join-Path $folder ("$name." + [guid]::NewGuid().ToString('N') + '.download')
    try {
        $attempt = 0
        while ($true) {
            $attempt++
            try {
                Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
                break
            }
            catch {
                if ($attempt -ge 3) {
                    throw "Could not download $url ($($_.Exception.Message))."
                }

                Start-Sleep -Seconds (2 * $attempt)
            }
        }

        $actual = Get-Sha256 $temp
        if ($actual -ne $sha256) {
            throw "SHA-256 of $url is $actual, but Directory.Packages.props pins $sha256. The file was not used."
        }

        [System.IO.File]::Move($temp, $target, $true)
    }
    finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
}
finally {
    if ($held) {
        $lock.ReleaseMutex()
    }

    $lock.Dispose()
}

Write-Host "HeroesClientSDK $version -> $target (SHA-256 ok)."
