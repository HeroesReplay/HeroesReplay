# AGENTS.md

Contract for coding agents working in this repository. Code and this file win over chat history.

## Product

Windows-only automated spectator for Heroes of the Storm `.StormReplay` files: parse → score focus per second → drive the live client + optional OBS/Twitch.

Solution: `heroes-replay.slnx` (.NET 10 LTS). Projects: `HeroesReplay.CLI`, `HeroesReplay.Core`, `HeroesReplay.HeroesProfile.Client` (Kiota v1), `HeroesReplay.Tests`. There is no AutoSpectator project. Regenerate the Heroes Profile client with `tools/generate-heroesprofile-client.ps1`; do not csharpier `Generated/`.

## Before editing

1. Load the matching **repo skill** under `.grok/skills/` (dotnet, CLI, OBS, Twitch, op-service-account).
2. Prefer official **dotnet/skills** plugins for generic .NET work (see below).
3. Format with CSharpier. Do not hand-format.

```powershell
dotnet tool restore
dotnet csharpier format src
dotnet csharpier check src
dotnet build heroes-replay.slnx
dotnet test heroes-replay.slnx
```

`dotnet test` is **Unit only**. Integration and smoke are opt-in:

```powershell
dotnet test heroes-replay.slnx -p:TestCategory=Integration
dotnet test heroes-replay.slnx -p:TestCategory=Smoke
```

Secrets: skill `op-service-account`. On a new clone, set user env `OP_SERVICE_ACCOUNT` then `pwsh -File tools/fill-secrets-from-op.ps1`. Never commit or print resolved tokens.

CLI: skill `heroes-replay-cli`. Connectivity: `check`. Live spectator for agents: `heroesreplay mcp` (stdio MCP; status file `%LOCALAPPDATA%/HeroesReplay/status.json`). Spectator and MCP are **two processes**.

## Environments

| | **dev** | **live** |
| --- | --- | --- |
| Hostname | `ASA-SERVER` | `DESKTOP-8SJE72` |
| Role | Development VM (Unraid guest). Lighter GPU: Intel Arc A310. Keep QXL/VNC. | 24/7 Twitch stream box (`saltysadism`). |
| Repo | `C:\heroesreplay\HeroesReplay` | Same path. Do not clone elsewhere (OBS `Default.json` hard-codes it). |
| Stream | Do not treat as the 24/7 channel. OBS/Twitch here is for testing. | Production ingest. Do not experiment on the live stream. |
| Upgrades | Safe to stop spectate, rebuild, reboot the guest (not Unraid/Tower). | Schedule **downtime** before pull, rebuild, client/OBS upgrades, or reboots. |

Both are Windows 11. Use the **same directory tree** so spectate, downloads, and OBS assets match.

| Path | Purpose |
| --- | --- |
| `C:\heroesreplay\HeroesReplay` | Git clone |
| `C:\heroesreplay\Battle.net\Battle.net.exe` | Battle.net (`winget --location C:\heroesreplay\Battle.net`) |
| `C:\Program Files (x86)\Heroes of the Storm` | HotS install |
| `C:\heroesreplay\Replays` | `spectate file` queue (`Location:ReplaySource`) |
| `C:\heroesreplay\Data\Standard` | Heroes Profile downloaded `.StormReplay` files |
| `C:\heroesreplay\Data\Requests` | Twitch-requested downloads |
| `C:\heroesreplay\Data\Contexts` | Per-replay context + OBS recordings (`RecFilePath`) |
| `C:\heroesreplay\Data\HeroesData` | heroes-data JSON cache |
| `C:\heroesreplay\secrets` | Local backup of gitignored `appsettings.secrets.json` |
| `%USERPROFILE%\Documents\Heroes of the Storm\Interfaces` | AhliObs (`client configure`) |
| `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` | OBS collection from `obs/Default.json` |
| `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\basic.ini` | OBS profile from `obs/Default/basic.ini` |

`Location:DataDirectory` is `C:\heroesreplay\Data`. Contexts are `Data\Contexts` (not a sibling of Data). Do not copy OBS `service.json` (stream key).

Detect with `hostname`. If `DESKTOP-8SJE72`, ask before stopping `heroesreplay` / HotS / OBS, and do not start a Twitch stream from a test build. If `ASA-SERVER`, do not SSH to Unraid (`Tower` / 192.168.1.102), do not bind the host 3090/iGPU, and do not reboot Tower.

New machine: clone into `C:\heroesreplay\HeroesReplay`, then `pwsh -File tools/bootstrap-workstation.ps1` (skill `op-service-account`).

## Hard rules

- Target `net10.0-windows10.0.19041.0` for CLI/Core/Tests. WinRT OCR and BitBlt need the Windows TFM.
- File-scoped namespaces, usings outside the namespace, `using` declarations where they reduce nesting.
- Calculators implement `IFocusCalculator.Contribute(ReplayTimeline)`. Do not bring back `GetFocusPlayers(TimeSpan, Replay)` × PLINQ.
- Focus pipeline: parse replay → calculators offer weighted events → hold last winner across empty seconds → spectator looks up by OCR game timer until core kill. Do not recompute calculators in the live loop.
- Living units: `unit.IsAliveAt(now)` (`TimeSpanDied == null` means alive).
- OBS: websocket **5**, port **4455**, **one connection per replay** (`BeginSession` / `EndSession`). Do not connect-disconnect per request.
- Cache the Heroes of the Storm HWND after launch; do not `GetProcessesByName` on every keystroke.
- `spectate file` plays the queue **once**. Heroes Profile provider loops.
- Never commit `appsettings.secrets.json`, user `*.StormReplay` dumps, or large `*.mp4`.
- Package versions live in `Directory.Packages.props`. Do not pin .NET 11 / CommandLine 3 prereleases.
- Polly cache still uses v7 `Policy.CacheAsync`. Do not bump Polly to 8 without replacing that cache.

## Verification

- Analysis changes: `dotnet test` plus `calculators coordinates` when replay format is in play.
- OBS changes: `check obs`; keep scene/source names configurable; wait for `IsIdentified`.
- Twitch: `check twitch`. PubSub reward topics are obsolete; do not add new ListenToRewards usage.
- CLI command changes: Smoke tests + `--help` on the new command.

## Repo skills (this tree)

| Skill | Use when |
| --- | --- |
| `.grok/skills/heroes-replay-cli` | spectate, check, calculators, secrets, `op://` |
| `.grok/skills/op-service-account` | `OP_SERVICE_ACCOUNT`, fill secrets on a new clone |
| `.grok/skills/dotnet-10-csharpier` | SDK, slnx, CSharpier, TFM, test categories |
| `.grok/skills/obs-websocket-v5` | OBS Studio control, scenes, recording, `check obs` |
| `.grok/skills/twitch-integration` | TwitchLib, rewards, predictions, `check twitch` |

Slash: `/heroes-replay-cli`, `/op-service-account`, `/dotnet-10-csharpier`, `/obs-websocket-v5`, `/twitch-integration`.

## External skills worth installing

Official [.NET agent skills](https://github.com/dotnet/skills) (marketplace `dotnet/skills`):

| Plugin | Why it helps here |
| --- | --- |
| `dotnet` / `csharp-refactoring` | File-scoped namespaces, modern C# |
| `dotnet-upgrade` | Further TFM/language migrations |
| `dotnet-msbuild` | slnx, Directory.Build.*, build breaks |
| `dotnet-nuget` | Central package management |
| `dotnet-test` | xUnit runs and fixtures |
| `dotnet-diag` | GDI/process leaks, perf |

Grok Build bundled skills already useful: `review` (maintainability bar), `create-skill`, `long-running-background-tasks` (OBS/game process).

There is no high-quality public skill specifically for **obs-websocket 5** or **TwitchLib 3.x** — that is why the two repo skills exist. Twitch EventSub (not PubSub) is the long-term replacement for channel-point redemptions.
