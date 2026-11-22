# The release tag release.yml publishes: v<major>.<minor>.<patch> (Semantic Versioning 2.0.0).
#
# - The commit already carries a vX.Y.Z tag (a re-run of the same master push): that tag.
# - Otherwise the newest vX.Y.Z tag in the repository, bumped by -Bump:
#     major  1.4.2 -> 2.0.0   breaking: config, OBS scene contract or install layout changes
#                             that need a hand on the stream PC
#     minor  1.4.2 -> 1.5.0   new features (clips, camera, YouTube, overlays)
#     patch  1.4.2 -> 1.4.3   fixes, tuning, dependency bumps (the default)
# - -Bump comes from the release pull request's labels (release:major, release:minor,
#   release:patch), or from "+semver: major|minor|patch" in the merge commit message. With
#   neither, the release is a patch.
# - The tags before semantic versioning (v1.0.0-614 to v1.0.0-1137) are 1.0.0 prereleases and
#   are ignored here. With no vX.Y.Z tag yet, the base is 1.0.0 and the first semantic release is
#   at least v1.1.0. Every install on a v1.0.0-NNNN tag takes it as newer, with the old updater's
#   number-by-number compare and the SemVer one alike.
#
# Writes the tag to stdout, and version=<tag> to $env:GITHUB_OUTPUT when that is set.
param(
    [ValidateSet('', 'major', 'minor', 'patch')][string]$Bump = '',
    # Comma or newline separated pull request labels.
    [string]$Labels = '',
    [string]$Message = '',
    # Tags to consider. Default: every tag in the repository.
    [string[]]$Tags,
    # Tags on the commit being released. Default: git tag --points-at HEAD.
    [string[]]$HeadTags
)

$ErrorActionPreference = 'Stop'

$stable = '^[vV](0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'

function ConvertTo-Version([string]$Tag) {
    if ($Tag -and $Tag.Trim() -match $stable) {
        return [pscustomobject]@{ Major = [long]$Matches[1]; Minor = [long]$Matches[2]; Patch = [long]$Matches[3] }
    }
}

function Format-Version($Version) {
    "v$($Version.Major).$($Version.Minor).$($Version.Patch)"
}

function Get-Newest([string[]]$Names) {
    $Names | ForEach-Object { ConvertTo-Version $_ } | Where-Object { $_ } |
        Sort-Object -Property Major, Minor, Patch | Select-Object -Last 1
}

if ($null -eq $Tags) {
    $Tags = @(git tag --list)
    if ($LASTEXITCODE -ne 0) {
        throw "git tag --list exited $LASTEXITCODE"
    }
}

if ($null -eq $HeadTags) {
    $HeadTags = @(git tag --points-at HEAD)
    if ($LASTEXITCODE -ne 0) {
        throw "git tag --points-at HEAD exited $LASTEXITCODE"
    }
}

$current = Get-Newest $HeadTags
if ($current) {
    $tag = Format-Version $current
    Write-Host "The commit is already released as $tag."
}
else {
    if (-not $Bump) {
        $names = @($Labels -split '[,\r\n]+' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
        foreach ($level in 'major', 'minor', 'patch') {
            if ($names -contains "release:$level" -or $Message -match "\+semver:\s*$level\b") {
                $Bump = $level
                break
            }
        }
    }

    if (-not $Bump) {
        $Bump = 'patch'
    }

    $newest = Get-Newest $Tags
    if (-not $newest) {
        # First semantic release after the v1.0.0-NNNN prereleases.
        $newest = [pscustomobject]@{ Major = 1L; Minor = 0L; Patch = 0L }
        if ($Bump -eq 'patch') {
            $Bump = 'minor'
        }
    }

    $next = switch ($Bump) {
        'major' { [pscustomobject]@{ Major = $newest.Major + 1; Minor = 0L; Patch = 0L } }
        'minor' { [pscustomobject]@{ Major = $newest.Major; Minor = $newest.Minor + 1; Patch = 0L } }
        default { [pscustomobject]@{ Major = $newest.Major; Minor = $newest.Minor; Patch = $newest.Patch + 1 } }
    }

    $tag = Format-Version $next
    Write-Host "Next release: $tag ($Bump bump from $(Format-Version $newest))."
}

if ($env:GITHUB_OUTPUT) {
    "version=$tag" >> $env:GITHUB_OUTPUT
}

Write-Output $tag
