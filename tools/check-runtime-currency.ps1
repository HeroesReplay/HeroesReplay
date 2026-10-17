# Compares the .NET runtime the latest GitHub Release names (the "Runtime:" line release.yml
# writes from the zip's runtime.txt) with the .NET release metadata, and keeps one issue open
# while a newer .NET security release is out (#462). .github/workflows/runtime-currency.yml runs
# it weekly; run it by hand with -DryRun to see what it would do without touching an issue.
# - A newer security release (releases.json "security": true): opens the issue, or updates the
#   open one (and comments when the newest security release changed since it was written).
# - The release carries the newest security runtime or a later one: closes the open issue.
# - The release names no runtime (from before #462): reports it and changes nothing.
# Needs gh signed in (GH_TOKEN in Actions) with issues: write.
param(
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Channel = '10.0',
    # Compare this runtime ('.NET 10.0.12') instead of the one the latest release names: a what-if
    # for -DryRun, such as for a release from before #462.
    [string]$ShippedRuntime = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $Repository) {
    throw 'Pass -Repository owner/name (GITHUB_REPOSITORY in Actions).'
}

$metadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$Channel/releases.json"
$titlePrefix = 'Stale .NET runtime:'
$marker = '<!-- runtime-currency: .github/workflows/runtime-currency.yml (#462) -->'

# 10.0.13 from ".NET 10.0.13" or "10.0.13"; prerelease suffixes are ignored. Null without one.
function Get-RuntimeVersion([string]$Text) {
    if ("$Text" -match '(\d+)\.(\d+)\.(\d+)') {
        return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }

    return $null
}

function Write-Summary([string]$Text) {
    Write-Host $Text
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $Text
    }
}

$metadata = Invoke-RestMethod -Uri $metadataUrl
$latestRuntime = "$($metadata.'latest-runtime')"
$release = gh release view --repo $Repository --json tagName,body,url,publishedAt | ConvertFrom-Json
$tag = $release.tagName

$releaseNotes = if ($ShippedRuntime) { "Runtime: $ShippedRuntime" } else { "$($release.body)" }
if ($releaseNotes -notmatch '(?m)^Runtime: (?<runtime>\.NET \S+)\s*$') {
    Write-Summary "Release $tag names no .NET runtime in its notes (a release from before #462). The newest .NET $Channel runtime is $latestRuntime. Nothing to compare; no issue changed."
    exit 0
}

$shippedText = $Matches.runtime
$shipped = Get-RuntimeVersion $shippedText
if (-not $shipped) {
    throw "Release $tag names runtime '$shippedText', which has no version."
}

# Every security release newer than the shipped runtime, oldest first.
$newer = @(
    $metadata.releases |
        Where-Object { $_.security -eq $true } |
        Where-Object { (Get-RuntimeVersion $_.runtime.version) -gt $shipped } |
        Sort-Object { Get-RuntimeVersion $_.runtime.version }
)

$open = @(
    gh issue list --repo $Repository --state open --limit 500 --json number,title,url |
        ConvertFrom-Json |
        Where-Object { $_.title.StartsWith($titlePrefix, [System.StringComparison]::Ordinal) }
)
$existing = $open | Select-Object -First 1

if ($newer.Count -eq 0) {
    $message = "Release $tag carries $shippedText. No newer .NET $Channel security release (latest runtime $latestRuntime)."
    Write-Summary $message
    if ($existing) {
        if ($DryRun) {
            Write-Host "Dry run: would close #$($existing.number) ($($existing.url))."
        }
        else {
            gh issue close $existing.number --repo $Repository --comment "$message Closed by the runtime-currency workflow." | Out-Null
            Write-Summary "Closed #$($existing.number)."
        }
    }

    exit 0
}

$newest = $newer[-1]
$newestVersion = "$($newest.runtime.version)"
$title = "$titlePrefix release $tag carries .NET $shipped; security release .NET $newestVersion is out"

$rows = foreach ($item in $newer) {
    $date = [datetime]::Parse($item.'release-date', [System.Globalization.CultureInfo]::InvariantCulture)
    $cves = @($item.'cve-list').Where({ $_ })
    $due = if ($cves.Count -gt 0) { $date.AddHours(72) } else { $date.AddDays(7) }
    $why = if ($cves.Count -gt 0) { "72 h, $($cves.Count) CVE(s)" } else { '7 days' }
    $notes = if ($item.'release-notes') { "[notes]($($item.'release-notes'))" } else { '' }
    "| $($item.runtime.version) | $($item.sdk.version) | $($date.ToString('yyyy-MM-dd')) | $why $notes | $($due.ToString('yyyy-MM-dd')) |"
}

$body = @"
$marker
The latest release, [$tag]($($release.url)), was built and tested on **$shippedText** (the ``Runtime:`` line of its notes, from ``runtime.txt``). .NET has shipped newer security releases since:

| Runtime | SDK | Released | Window | Due |
| --- | --- | --- | --- | --- |
$($rows -join "`n")

**Cadence** (``docs/runtime-currency.md``): merge Dependabot's SDK bump (``global.json``) and its ``microsoft-extensions`` group on ``develop`` within 7 days of Patch Tuesday, within 72 hours when the release notes list a CVE. Let CI build and smoke it, prove it on ASA-SERVER, then release ``develop`` to ``master``.

While the release is framework-dependent, the stream PC runs on its machine-wide .NET runtime, which Microsoft Update services: ``heroesreplay check runtime`` there shows which one. A self-contained or Native AOT release (#453, #461) carries the runtime above until the next release.

Checked $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC against [releases.json]($metadataUrl) (latest runtime $latestRuntime). The workflow closes this issue once the latest release carries .NET $newestVersion or later.
"@

Write-Summary "Release $tag carries $shippedText; security release .NET $newestVersion is out ($($newer.Count) newer security release(s))."
if ($DryRun) {
    $action = if ($existing) { "update #$($existing.number)" } else { 'open an issue' }
    Write-Host "Dry run: would $action titled '$title' with:"
    Write-Host $body
    exit 0
}

$bodyFile = New-TemporaryFile
try {
    Set-Content -LiteralPath $bodyFile -Value $body -Encoding utf8NoBOM
    if ($existing) {
        gh issue edit $existing.number --repo $Repository --title $title --body-file $bodyFile | Out-Null
        if ($existing.title -cne $title) {
            gh issue comment $existing.number --repo $Repository --body "Security release .NET $newestVersion is out; the table above is updated." | Out-Null
        }

        Write-Summary "Updated #$($existing.number) ($($existing.url))."
    }
    else {
        $url = gh issue create --repo $Repository --title $title --body-file $bodyFile --label dependencies
        Write-Summary "Opened $url."
    }
}
finally {
    Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
}
