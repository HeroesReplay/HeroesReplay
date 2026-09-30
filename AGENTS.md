# AGENTS.md

Contract for coding agents working in this repository. Code and this file win over chat history.

## Product

Windows-only automated spectator for Heroes of the Storm `.StormReplay` files: parse → score focus per second → drive the live client + optional OBS/Twitch. Product vision and the 24/7 cycle: `docs/vision.md`.

Solution: `heroes-replay.slnx` (.NET 10 LTS). Projects: `HeroesReplay.CLI`, `HeroesReplay.Core`, `HeroesReplay.HeroesProfile.Client` (Kiota v1), `HeroesReplay.Tests`. There is no AutoSpectator project. Regenerate the Heroes Profile client with `tools/generate-heroesprofile-client.ps1`; do not csharpier `Generated/`.

## Before editing

1. Load the matching skill under `.grok/skills/` (HeroesReplay skills and the vendored official .NET skills).
2. For generic .NET work, use the vendored `dotnet/skills` set in that folder (`csharp-refactoring`, MSBuild, NuGet, test, diagnostics, upgrade).
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

## Purpose of the work

Improve the spectator and the tools around it. The work is to prove, validate, and keep the functionality correct, and to make the CLI, the services, and the spectator more resilient and easier to run.

**ASA-SERVER is the development machine.** Sessions here exist only to test a change. Stop the process when the check is done. Do not leave a match, or the game client, running for its own sake. Do not treat this box as the broadcast.

**DESKTOP-8SJE72 is the production machine.** That is where real spectating and the live stream happen. Do not kill, rebuild, or experiment there without a scheduled downtime.

## Dev loop

One task at a time. A GitHub issue, or one concrete bug. Do not start a second task, and do not merge to `master`, while it is open. Branch work lands on `develop`.

Stop at the earliest phase that can prove the change.

1. **Unit.** Change the type that owns the behavior and call that type from a test. `dotnet test` is Unit only. This is enough for parse rules, reward titles, prediction decisions, queue locking, and probe decisions.
2. **Build.** A running `heroesreplay.exe` locks the Debug bin (`MSB3027`). Stop with `heroesreplay services stop`. That asks the four processes to exit, then closes Heroes of the Storm. If you stop the processes yourself, close the game too: `CloseMainWindow`, then `Kill` if it is still there after a few seconds. An open client with no spectator is a stuck replay, not a test.
3. **Short live proof.** Only when the change touches launch, the HUD clock, hero selection, the end screen, OBS scenes, download pacing, or predictions. One replay until the clock reads `MM:SS` and a hero is selected (`1`–`0`), then `services stop`. Streaming stays off on ASA-SERVER.
4. **Long proof, 1 to 5 games.** Only when the user asks to prove the loop, or the change is end-of-match, the next replay loading, request-before-Standard, or a prediction opening and resolving. Each counted game reaches the core (HUD time within a second or two of core death) and the next replay starts loading. Do not kill the stack mid-game to rebuild. Do not continue past five. Then `services stop`, and confirm Heroes of the Storm is gone.

### Current patch and previous patch

Both of these are acceptance criteria for a local end-to-end run. A run that only proves one of them is not done.

The newest installed `Versions\Base*\HeroesOfTheStorm_x64.exe` is the current patch. An older exe in that folder is a previous patch. A `Base*` folder with no executable is not installed (an empty `Base98297` does not count). `ReplayClientRoute` chooses the launch. The running exe file version must equal the replay version before the match clock counts.

- **Current patch.** Ask the logged-in Battle.net to launch Hero (`--exec="launch Hero"`, which is `-sso=1`). Do not open the `.StormReplay` first. A direct HeroesSwitcher open starts the game without SSO and stops on the email/password form. Wait for the signed-in home screen, then open the replay. A login form means that process was not signed in: close it and ask Battle.net again once. Do not type credentials. Do not click Update.
- **Previous patch.** Do not ask Battle.net to launch Hero and do not click Update. Play always starts the newest client, and the older replay then hits "Game client version mismatch". If a different build is running, close it and wait for Battle.net to finish that shutdown. Open the replay through HeroesSwitcher so it starts that older `Versions\Base*` exe. A login form on that older exe is not fixed by launching Hero: close it and open the replay through the switcher once more. The older client may load the replay without a home screen. A loading screen or match clock on the matching exe is success. If it does stop on the home screen, open the replay from there. When that matching exe is already up without the home screen or the match clock, open the replay on that exe. Do not leave the file closed, and do not ask Battle.net. HeroesSwitcher is still the first open when no matching client is running.
- **Missing build.** Do not launch the current client and do not open the file. The replay stays queued.
- **Patch line.** A main patch is the first two numbers (`2.57.*`). Every build iteration of that line is its own client: `2.57.0.98285`, `2.57.0.98304`, and later `2.57.*` builds. They are not interchangeable. The replay build must equal the exe. The newest installed exe is still the current patch and signs in through Battle.net. Each older iteration is a previous patch and opens through HeroesSwitcher. `heroesprofile patch-index` reports the first replay id of that whole line, not only the newest build number.
- **Reclaim.** Battle.net deletes the previous iteration's `Versions\Base*` exe while it installs the next one. An empty `Base*` folder is still not a client. Before a replay launches, copy each supported exe into `Location:RetainedClientDirectory` (`Data\Clients` when that is empty). When the live exe for the replay's build is missing and a copy exists, copy it back into `Versions\Base*` before HeroesSwitcher runs. A build deleted before any copy was kept stays not installed. Do not click Update or Play to fetch it. The retained set is every installed build on the `MinimumGameVersion` patch line, plus any installed build at or above that floor.
- **Firewall.** Windows asks to allow public and private networks the first time each `Versions\Base*\HeroesOfTheStorm_x64.exe` listens. Before Battle.net or HeroesSwitcher starts Heroes, add an inbound allow rule for every installed client exe. Do not click Allow. The rule is for that exe path, so a new `Base*` folder needs its own rule.
- **New client.** When a newer `Base*` exe is installed, write windowed 1080p and AhliObs into root and account `Variables.txt` and copy the interface again before that client is treated as ready, even if the files already name AhliObs. If the exe whose file version matches the replay shows "All data files must be fully downloaded", wait for that screen to clear, close Heroes only, write AhliObs again, and start that client once more before the replay stays up. HeroesSwitcher opens the newest exe first so it can start an older build. A download dialog on that newer exe is the handoff. It does not arm the AhliObs restart, and that restart does not run when the replay's exe then appears. A blank matching client after that handoff is started again directly with the replay path. A HeroesSwitcher process with no Heroes process is closed before the next open. The download screen is stock chrome. Do not click Play, Update, or Allow.

A version-mismatch or region-unavailable dialog on the client that already matches the replay still leaves that client open and requeues the replay. On ASA-SERVER the long proof includes one current-patch replay and one previous-patch replay whenever both clients are installed. Each must reach the match clock on its own exe. Test uploads stay private and titled with `[TEST]`. Do not set `HEROES_REPLAY_ENV=prod`. Do not start Twitch ingest.

Live-proof logs: Aspire dashboard at `http://127.0.0.1:18888`. The Aspire CLI on this machine is 13.5.4 and its MCP server is `aspire agent mcp`. This app has no AppHost, so connect it in dashboard-only mode: `aspire agent mcp --dashboard-url http://127.0.0.1:18888`. That mode exposes only `list_structured_logs`, `list_traces`, and `list_trace_structured_logs`. `list_resources` and `execute_resource_command` need an AppHost and are not available here. If that MCP is not connected, use `aspire otel logs`. Spectate, Twitch, download, and YouTube. No unhandled error stacks. No repeated invalid-timer flood. Service consoles must not cover the clock pill.

On ASA-SERVER, after a spectator, OCR, OBS, or Twitch change: `services stop` (this closes HotS), `dotnet build heroes-replay.slnx -c Release`, copy `appsettings.secrets.json` into the CLI Release bin, and start a proof only if phase 3 or 4 applies. Never start Twitch ingest here.

### Capture

`IGameCapture` is the capture port. `PrintWindowCapture` is the default (`Capture:Method` = `PrintWindow`): the game HWND's composed frame, cropped to the client area, including DirectX. Another window on top of the game does not replace the clock. `BitBltCapture` is the desktop copy and is only used when `Capture:Method` is `BitBlt`. Windowed mode is still required so DWM has a frame.

OBS game capture also sees the frame when the window is covered, because it hooks the swap chain. One-off `GetSourceScreenshot` calls have worked. A sustained one-screenshot-per-second measurement has **not** been done. That comparison is issue 27. The dynamic memory scan stays off. Build `2.55.17.98025` also has a fixed read-only tick address (`MatchTickClock`, seconds = ticks / 4096). It is preferred when that read succeeds. OCR of `-MM:SS` or `MM:SS` remains the fallback. A different client build does not use those offsets.

## Environments

| | **dev** | **live** |
| --- | --- | --- |
| Hostname | `ASA-SERVER` | `DESKTOP-8SJE72` |
| Role | Develop, prove, and harden the spectator, CLI, and services. Not a broadcast. | Real spectating and the 24/7 Twitch stream (`saltysadism`). Intel Arc A310 guest vs this live box. |
| Repo | `C:\heroesreplay\HeroesReplay` | Same path. A release install rewrites OBS assets under its own `obs` folder (`C:\heroesreplay\app\obs`), not this checkout. |
| Stream | **Do not go live.** OBS, predictions, chat, rewards, and requested-replay recording/YouTube are for testing only. | Production ingest. Do not experiment on the live stream. |
| Upgrades | Safe to stop spectate, rebuild, reboot the guest (not Unraid/Tower). | Schedule **downtime** before pull, rebuild, client/OBS upgrades, or reboots. |
| Spectator engine | **Normal** to kill `heroesreplay`, quit HotS, rebuild Release, and relaunch only long enough to prove a change. Stop when the proof is done. | Do **not** kill/rebuild/restart the spectator as a routine. This is the production spectate. |

On **ASA-SERVER**, prove a change with a short run, then read `%LOCALAPPDATA%\HeroesReplay\status.json` and `Data\Contexts\<id>\end.png` if the client was closed. Do not keep spectating after that. On **DESKTOP-8SJE72**, ask before stopping anything.

### Maps, modes, client

- Loop: Storm League, current patch. Rewards: QM, SL, ARAM. Not Unranked Draft (removed from the client) or brawls (`Maps:Catalog` `Playable: false`).
- Ranked/QM maps: Infernal Shrines, Sky Temple, Cursed Hollow, Dragon Shire, Towers of Doom, Tomb of the Spider Queen, Volskaya Foundry, Garden of Terror, Blackheart's Bay, Warhead Junction, Alterac Pass, Battlefield of Eternity, Hanamura Temple, Haunted Mines, Braxis Holdout.
- ARAM: Silver City, Lost Cavern, Industrial District, Braxis Outpost.
- ReplayId rewards: the current patch line (`MinimumGameVersion` and every newer build, including each `2.57.*` iteration). A reward still needs that build's exe. Older replays need a local `Versions\Base*` folder; old clients are often no longer downloadable.
- Spectator keys: `1`–`0` observe player. Do not send `C` (follow player camera) or Shift+Z ultra zoom.
- Twitch: chat + PubSub reconnect with backoff; Helix predictions; channel-point rewards queue `Data\requests.json`. Reward prompts must say recent patch ReplayIds.

### Calculators

`Calculators:Enabled` in appsettings (type name, default on) for A/B. Example: `"EmotingCalculator": false`.

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
| `.grok/skills/ffmpeg` | Cut pentakill clips from OBS recordings, full 1920x1080 frame. Binary is ffmpeg 9.0.2 at `C:\ffmpeg\bin`. |
| `.grok/skills/release-install` | Install production from the GitHub Release zip instead of cloning and building. |

Slash: `/heroes-replay-cli`, `/op-service-account`, `/dotnet-10-csharpier`, `/obs-websocket-v5`, `/twitch-integration`, `/ffmpeg`, `/release-install`. `csharp-solid` is also in this folder.

## Official .NET skills

These are installed under `.grok/skills/<name>/` from [dotnet/skills](https://github.com/dotnet/skills) commit `e115891bd2ac` (MIT, `.grok/skills/dotnet-skills.LICENSE.txt`). Load the skill whose name matches the task. Each `SKILL.md` is the source of truth for when to use it.

| Plugin | Skills |
| --- | --- |
| `dotnet` | `csharp-refactoring`, `setup-local-sdk` |
| `dotnet-upgrade` | `dotnet-aot-compat`, `migrate-dotnet8-to-dotnet9`, `migrate-dotnet9-to-dotnet10`, `migrate-dotnet10-to-dotnet11`, `migrate-nullable-references`, `thread-abort-migration` |
| `dotnet-msbuild` | `binlog-failure-analysis`, `binlog-generation`, `build-parallelism`, `build-perf-baseline`, `build-perf-diagnostics`, `check-bin-obj-clash`, `copy-to-output-directory`, `directory-build-organization`, `eval-performance`, `extension-points`, `including-generated-files`, `incremental-build`, `item-management`, `msbuild-antipatterns`, `msbuild-modernization`, `property-patterns`, `resolve-project-references`, `target-authoring` |
| `dotnet-nuget` | `convert-to-cpm` |
| `dotnet-test` | `assertion-quality`, `code-testing-agent`, `code-testing-extensions`, `coverage-analysis`, `crap-score`, `detect-static-dependencies`, `filter-syntax`, `find-untested-sources`, `generate-testability-wrappers`, `grade-tests`, `migrate-static-to-wrapper`, `mtp-hot-reload`, `platform-detection`, `run-tests`, `scaffold-dotnet-test-project`, `test-analysis-extensions`, `test-anti-patterns`, `test-gap-analysis`, `test-smell-detection`, `test-tagging`, `testability-obstacle`, `writing-mstest-tests` |
| `dotnet-diag` | `analyzing-dotnet-performance`, `android-tombstone-symbolication`, `apple-crash-symbolication`, `clr-activation-debugging`, `dotnet-trace-collect`, `dump-collect`, `microbenchmarking` |

Grok already provides `review`, `create-skill`, and `long-running-background-tasks`. They stay with the tool and are not copied into this repo.

There is no high-quality public skill specifically for **obs-websocket 5** or **TwitchLib 3.x** — that is why the two repo skills exist. Twitch EventSub (not PubSub) is the long-term replacement for channel-point redemptions.
