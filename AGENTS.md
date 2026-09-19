# AGENTS.md

Contract for coding agents working in this repository. Code and this file win over chat history.

## Product

Windows-only automated spectator for Heroes of the Storm `.StormReplay` files: parse → score focus per second → drive the live client + optional OBS/Twitch.

Solution: `heroes-replay.slnx` (.NET 10 LTS). Projects: `HeroesReplay.CLI`, `HeroesReplay.Core`, `HeroesReplay.Tests`. There is no AutoSpectator project.

## Before editing

1. Load the matching **repo skill** under `.grok/skills/` (dotnet, CLI, OBS, Twitch).
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

Heroes Profile integration uses `HEROES_REPLAY_HeroesProfileApi__ApiKey` or `op read op://Private/Heroes Profile API/password`. Never commit the resolved token.

CLI: skill `heroes-replay-cli`. Connectivity: `check`. Live spectator for agents: `heroesreplay mcp` (stdio MCP; status file `%LOCALAPPDATA%/HeroesReplay/status.json`). Spectator and MCP are **two processes**.

## Hard rules

- Target `net10.0-windows10.0.19041.0` for CLI/Core/Tests. WinRT OCR and BitBlt need the Windows TFM.
- File-scoped namespaces, usings outside the namespace, `using` declarations where they reduce nesting.
- Calculators implement `IFocusCalculator.Contribute(ReplayTimeline)`. Do not bring back `GetFocusPlayers(TimeSpan, Replay)` × PLINQ.
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
| `.grok/skills/dotnet-10-csharpier` | SDK, slnx, CSharpier, TFM, test categories |
| `.grok/skills/obs-websocket-v5` | OBS Studio control, scenes, recording, `check obs` |
| `.grok/skills/twitch-integration` | TwitchLib, rewards, `check twitch` |

Slash: `/heroes-replay-cli`, `/dotnet-10-csharpier`, `/obs-websocket-v5`, `/twitch-integration`.

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
