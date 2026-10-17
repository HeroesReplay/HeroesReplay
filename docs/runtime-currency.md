# Keeping the .NET runtime current

Step 9 of the Native AOT epic (#452), issue #462. It must be in place before any release that carries its own runtime.

## Why

Today `heroesreplay.exe` is framework-dependent. It runs on the machine-wide .NET 10 runtime, which Microsoft Update services: a .NET security fix reaches the stream PC without a release. Windows Update can also remove that runtime under running roles (#450, 2026-10-15).

A self-contained release (#453) or a Native AOT release (#461) carries its runtime inside the zip. A .NET security fix then reaches production only when CI rebuilds the release with a newer SDK and `develop` is released to `master`. This page is how that happens on time.

## What is pinned

| What | Where | Moved by |
| --- | --- | --- |
| The .NET SDK | `global.json` (`10.0.402`, `rollForward: latestPatch`) | Dependabot `dotnet-sdk`, weekly |
| `Microsoft.Extensions.*` and `System.*` (the 10.0.x servicing train) | `Directory.Packages.props` | Dependabot `nuget`, weekly, one `microsoft-extensions` pull request |
| OpenTelemetry, Kiota | `Directory.Packages.props` | Dependabot `nuget`, weekly, one pull request per group |
| Every other package | `Directory.Packages.props` | Dependabot `nuget`, weekly, one pull request each |
| `HeroesClientSDK` | `Directory.Packages.props` (exact pin plus SHA-256) | By hand only. A bump is a match-clock change and needs the live proof (AGENTS.md) |
| Workflow actions | `.github/workflows/*.yml` | Dependabot `github-actions`, monthly |

- **The SDK.**
  - `ci.yml` and `release.yml` install it with `actions/setup-dotnet` and `global-json-file: global.json`.
  - `tools/bootstrap-workstation.ps1` warns when no installed SDK satisfies the pin.
  - The SDK brings the runtime the build is tested on. Native AOT also bakes that runtime patch in, so the SDK bump pull request is the record of which runtime ships.
  - `latestPatch` lets a runner use a newer preinstalled patch of the same feature band (10.0.4xx for 10.0.402). `runtime.txt` shows which runtime was used. Set `"rollForward": "disable"` if exact reproducibility matters more than a runner that already has a newer patch.
- **Majors.** Dependabot ignores major versions of `Microsoft.Extensions.*` and `System.*`. That keeps the packages on the .NET 10 LTS line and keeps `System.CommandLine` on 2 (AGENTS.md hard rules).
- **Security updates.** Turn on Dependabot security updates in the repository settings (Code security). That is a setting, not a file.

## Cadence

.NET servicing ships on Patch Tuesday, the second Tuesday of each month. Dependabot runs on Wednesday, so the SDK and `microsoft-extensions` pull requests open the day after.

1. **Within 7 days of Patch Tuesday**, merge the SDK bump (`global.json`) and the `microsoft-extensions` group on `develop`.
   - `ci` builds, runs Unit and Smoke, packages the zip, and checks its `runtime.txt` (`tools/verify-release.ps1`).
   - With Native AOT, `ci` also runs the AOT build and its smoke (#460).
2. **Prove it on ASA-SERVER**, then release `develop` to `master` (AGENTS.md Dev loop). The release notes name the runtime.
3. **When the .NET release notes list a CVE**, do steps 1 and 2 within 72 hours of the release.

Each month's release metadata says whether the release is a security release and lists its CVEs: `https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json` (`security`, `cve-list`, `release-notes`).

## Where the runtime shows

The runtime is `RuntimeInformation.FrameworkDescription`, such as `.NET 10.0.13`.

| Where | What it is |
| --- | --- |
| `runtime.txt` in the release zip, beside `version.txt` | The runtime the packaged exe ran on when `tools/package-release.ps1` asked it (`heroesreplay check runtime`). `version.txt` stays the tag alone, because the update compares it to the release tag. |
| The GitHub Release notes | `Runtime: .NET 10.0.13` and `SDK: 10.0.402`, from `release.yml`. The weekly check reads the `Runtime:` line. |
| `heroesreplay check runtime` | The runtime this exe runs on, its folder, and the release's `runtime.txt`. A warning (`check.runtime.older_than_release`) when this machine's runtime is older than the release's: Microsoft Update has not serviced it yet. |
| `status.json` | `dotnetRuntime`: the runtime spectate runs on. |
| `services status` | Each role's `dotnetRuntime`, from its heartbeat, and the top-level `dotnetRuntime` (what a role started now loads) and `releaseRuntime` (`runtime.txt`). The text output prints a `Runtime:` line, and the runtime after each role's version. |
| MCP `get_spectator_status` | `status.dotnetRuntime` (spectate's), `dotnetRuntime` (the MCP server's), and `releaseRuntime`. |

While the release is framework-dependent, `runtime.txt` is the runtime CI built and tested it on. The stream PC runs on its own machine-wide runtime, which can be newer, or older until Microsoft Update services it. `check runtime` on the stream PC says which.

## The weekly check

`.github/workflows/runtime-currency.yml` runs every Wednesday, and on demand (`workflow_dispatch`). It runs `tools/check-runtime-currency.ps1`, which:

- Reads the `Runtime:` line of the latest GitHub Release. A release from before #462 has none: the run says so and changes nothing.
- Reads `latest-runtime` and each release's `security` flag from the .NET 10.0 `releases.json`.
- Opens one issue, titled `Stale .NET runtime: ...` and labelled `dependencies`, when a security release is newer than the release's runtime. The issue has a table of each newer security release, its SDK, its CVE count and its due date (72 hours with CVEs, else 7 days).
- Updates that issue on later runs, and comments when a newer security release came out since.
- Closes it once the latest release carries the newest security runtime or a later one.

Run it by hand without touching an issue:

```powershell
pwsh -File tools/check-runtime-currency.ps1 -Repository HeroesReplay/HeroesReplay -DryRun
pwsh -File tools/check-runtime-currency.ps1 -Repository HeroesReplay/HeroesReplay -DryRun -ShippedRuntime ".NET 10.0.11"
```

`-ShippedRuntime` compares another runtime instead of the release's (a what-if).

## Rollback route

For the first self-contained or AOT releases, keep the framework-dependent publish as a CI artifact, so a release can go back to the machine-wide runtime (#461).
