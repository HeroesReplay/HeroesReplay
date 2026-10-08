# AGENTS.md

Contract for coding agents working in this repository. Code and this file win over chat history.

## Product

Windows-only automated spectator for Heroes of the Storm `.StormReplay` files: parse → score focus per second → drive the live client + optional OBS/Twitch. Product vision and the 24/7 cycle: `docs/vision.md`.

Solution: `heroes-replay.slnx` (.NET 10 LTS). Projects: `HeroesReplay.CLI`, `HeroesReplay.Core`, `HeroesReplay.HeroesProfile.Client` (Kiota v1), `HeroesReplay.Tests`. There is no AutoSpectator project. Regenerate the Heroes Profile client with `tools/generate-heroesprofile-client.ps1`; do not csharpier `Generated/`.

The client memory readers live in their own repo and package, [HeroesClientSDK](https://github.com/HeroesReplay/HeroesClientSDK) (namespace `HeroesClientSDK`, #274): `StableMatchClock`, `LoadingScreenMemory`, their samples, `ClockTelemetry`, and the per-build patterns. Change them there, with their tests, then tag `vX.Y.Z` on its `main`; `publish.yml` attaches `HeroesClientSDK.X.Y.Z.nupkg` to the GitHub Release of that tag (and also pushes it to GitHub Packages). HeroesReplay restores it without credentials: `nuget.config` maps only `HeroesClientSDK` to the gitignored `.packages` folder, and `tools/restore-sdk-package.ps1` downloads the release asset into it and refuses a file whose SHA-256 differs from the pin. `ci.yml` and `release.yml` run that script before anything restores, `bootstrap-workstation.ps1` runs it, and the build runs it when the file is missing (`Directory.Build.targets`). To bump, set `HeroesClientSDKVersion` and `HeroesClientSDKSha256` together in `Directory.Packages.props` (the SHA-256 of the release asset; `PackageVersion` is the exact pin `[$(HeroesClientSDKVersion)]`). A bump is a match-clock change: it needs the short live proof on ASA-SERVER (`check timer`, plus a current-patch and a previous-patch replay reaching `MM:SS`) before `master`. The stream PC needs nothing: the release zip carries `HeroesClientSDK.dll`.

### Source layout: feature slices

`HeroesReplay.Core` is grouped by feature, not by kind. There are no `Models/`, `Services/`, or `Extensions/` folders. A feature's types, settings class, interfaces, and extension methods sit together. The namespace is the folder (`Core/YouTube/Metadata` is `HeroesReplay.Core.YouTube.Metadata`). `Engine` and `IEngine` sit at the Core root. In `HeroesReplay.CLI`, `Commands/<Command>` holds a command and the types only it uses (`Commands/Mcp` is `heroesreplay mcp` and its tools), `OpenTelemetry` is the logging and tracing every command shares, and `Output` is the `--output text|json` contract every agent-facing command shares (`CliOutput`, `CliRedaction`; the envelope types are `Core/Shared/CliResult.cs`). `HeroesReplay.Tests/Unit/<Slice>[/<Sub>]` mirrors the slices: a test sits in the folder of the slice that owns the type it tests, and its namespace is that folder (`Unit/YouTube/Metadata` is `HeroesReplay.Tests.Unit.YouTube.Metadata`). `Unit/Check` and `Unit/Support` are the extra test folders. `Integration/<Slice>` follows the same names.

| Slice | Owns |
| --- | --- |
| `Analysis` (`Calculators`, `Reports`) | Replay timeline, focus calculators, `Focus`, `Panel`, kill streaks, calculator and weight settings |
| `Spectating` (`Session`, `Control`, `Clock`, `Screens`, `Capture`, `Reports`) | The live spectator loop (`Spectator`). `Session`: `GameManager` runs one replay from launch to the next replay's handoff; match outcome, session holds, report handoff, retry and shutdown. `Control`: `GameController`, window input, observer panels. `Clock`: `IGameTimer` and its log over the HeroesClientSDK memory match clock. `Screens`: which screen the client shows (map loading, home, end), memory first (HeroesClientSDK) and OCR when memory cannot tell. `Capture`: frame capture |
| `GameClient` (`Firewall`) | Launching the right Heroes build, Battle.net, HeroesSwitcher, the client's own dialogs, `Variables.txt`, client and process settings. `Firewall`: the inbound rule for each client exe |
| `Replays` (`Context`) | Replay providers and loaders, `LoadedReplay`, the spectate queue and queue pick, the per-replay context folder |
| `Requests` | Twitch request queue, leases, played ids, reward request models |
| `HeroesProfile` | Heroes Profile API, replay listing, patch index, rank enrichment, the hero statistics behind YouTube title hooks (`HeroStatsRefresh`, `HeroStatsStore`) |
| `HeroesData` | heroes-data2 hero and unit catalog |
| `Twitch` (`Predictions`, `Rewards`, `RedeemedRewards`, `ChatMessages`) | Chat bot, predictions and their ledger, channel-point rewards |
| `TwitchExtension` | Heroes Profile Twitch extension payloads |
| `Obs` (`Collection`, `Inspection`, `Recording`, `Pages`) | OBS websocket control, the stream arm, report scenes. `Collection`: the scene collection and profile files OBS reads. `Inspection`: the read-only reads behind `obs inspect`, `obs validate`, and the MCP tools. `Recording`: the match recording and its HUD clock. `Pages`: `obs pages` |
| `YouTube` (`Metadata`, `Publication`, `Playlists`, `Search`, `Outbox`, `Quota`) | Upload, titles and descriptions, publication budget, playlists, duplicate lookup, quota units |
| `MediaPolicy`, `Clips`, `Retention` | What gets recorded and uploaded, pentakill detection and clips (`FfmpegLocator`, `check ffmpeg`), disk cleanup |
| `Dependencies` | The pinned external tools (`dependencies.json`, embedded: ffmpeg and ffprobe) and `deps install`: download, size and SHA-256 check, staged install into `Dependencies:Directory` |
| `Connectivity`, `ServiceHost` (`Logs`), `SelfUpdate`, `Status` | Outage handling, the four service processes and their role logs, release updates, `status.json` |
| `Telemetry` | `HeroesReplayTelemetry` (traces and metrics) and `ReplaySessionFile`, which joins one replay's traces across processes |
| `Shared` | Types used across slices: `Map`, `Hero`, `GameType`, `GameRank`, `EnglishMapNames`, `DurableFile`, `NamedProcess`, `ProcessTable` (parent pids, image paths, start times), resilience and secrets helpers |
| `Configuration` | `AppSettings` (the root that binds every slice's settings) and `LocationSettings` |

Put a new type in the slice that uses it. Move it to `Shared` only when two or more slices need it.

## Before editing

1. Load the matching skill under `.agents/skills/` (HeroesReplay skills and the vendored official .NET skills).
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
pwsh -File tools/verify.ps1
```

`tools/verify.ps1` is the git hook: pre-commit, and pre-push when the update is `develop`. It builds (unused usings and unused private members are errors in `.editorconfig`) and runs `Category=Unit` only. `UnusedSourceTests` fails when a Core or CLI type is not reachable from startup, tests, or the reward/chat handler scan. `HEROESREPLAY_SKIP_VERIFY=1` or `--no-verify` skips the hook. Kiota `Generated/` is not part of that unused-code check.

Secrets: skill `op-service-account`. On a new clone, set user env `OP_SERVICE_ACCOUNT` then `pwsh -File tools/fill-secrets-from-op.ps1`. Never commit or print resolved tokens.

CLI: skill `heroes-replay-cli`. Connectivity: `check`. Live spectator for agents: `heroesreplay mcp` (stdio MCP; status file `%LOCALAPPDATA%/HeroesReplay/status.json`). Spectator and MCP are **two processes**.

OBS ownership, updates, validation, and the machine profile policy: `docs/obs-operations.md`. MCP discovery: `.mcp.json` at the repo root (source, `dotnet run`) and in the release zip (`heroesreplay.exe mcp`).

Production MCP is read-only. `heroesreplay mcp` reads OBS with `obs_inspect`, `obs_validate`, and `obs_screenshot`, which send only Get requests (`ObsReadOnly`) and never return the stream key. Nothing in it starts or stops a stream or recording, changes a scene, or arms a machine; that stays with guarded CLI commands. obs-mcp (royshil, about 120 unrestricted tools, returns the stream key) is **dev-only**: register it per machine on ASA-SERVER (`claude mcp add obs --scope user …`) if wanted. Never add it to the repo, the release zip, or DESKTOP-8SJEK72.

## Purpose of the work

Improve the spectator and the tools around it. The work is to prove, validate, and keep the functionality correct, and to make the CLI, the services, and the spectator more resilient and easier to run.

**ASA-SERVER is the development machine.** Sessions here exist only to test a change. Stop the process when the check is done. Do not leave a match, or the game client, running for its own sake. Do not treat this box as the broadcast.

**DESKTOP-8SJEK72 is the production machine.** That is where real spectating and the live stream happen. Do not kill, rebuild, or experiment there without a scheduled downtime.

## Dev loop

One task at a time. A GitHub issue, or one concrete bug. Do not start a second task, and do not merge to `master`, while it is open.

`develop` is the GitHub default branch. A clone checks it out, and branch work lands there. `master` is only for a production build: the win-x64 executable, prod settings, and the OBS collection. Merging to `master` is what publishes that release. Do not put day-to-day commits on `master`.

Changes reach `develop` only through pull requests. The `ci` workflow (`.github/workflows/ci.yml`, job `build-and-test`: CSharpier check, Release build, Unit and Smoke tests, then the release zip from `tools/package-release.ps1` checked by `tools/verify-release.ps1`) runs on every pull request and every push to `develop`, and the branch rules require it. Open the PR and turn on auto-merge (`gh pr merge <n> --auto --merge`); GitHub merges once `build-and-test` passes, and deletes the branch. A red check blocks the merge: fix it on the branch. Release by opening a pull request from `develop` to `master` when ASA-SERVER has proven the build; the same check runs there, and the merge runs `release.yml`. The stream PC installs each release between replays (`Release:Enabled` in prod): a match ends, the release is staged before the next replay is picked, the report plays out onto the waiting scene (a live stream stays up on it), and the new build launches the next replay. `apply-release.ps1` first mirrors the running install into `app.previous` (it just finished a replay), then gates the new one: every role heartbeating from the new install and spectate reaching a match clock within `Release:HealthWindow` (20 min). Healthy refreshes `app.previous`. Unhealthy (a role down or stale, or every replay spectate tried failed) is rolled back to `app.previous` with the OBS scene collection that build ran with (`update restore-obs`, live-swapped while OBS streams), restarted the same way (supervised if it was), and its tag goes into `%LOCALAPPDATA%\HeroesReplay\updates\skipped-releases.txt`. Inconclusive (nothing playable: empty queue, outage, only held replays) keeps the install (skill `release-install`).

Stop at the earliest phase that can prove the change.

1. **Unit.** Change the type that owns the behavior and call that type from a test. `dotnet test` is Unit only. This is enough for parse rules, reward titles, prediction decisions, queue locking, and probe decisions.
2. **Build.** A running `heroesreplay.exe` locks the Debug bin (`MSB3027`). Stop with `heroesreplay services stop`. That asks the four processes to exit, then closes Heroes of the Storm. Exit 1 means a role, the game, or the OBS stream is still up; its output names which. If you stop the processes yourself, close the game too: `CloseMainWindow`, then `Kill` if it is still there after a few seconds. An open client with no spectator is a stuck replay, not a test.
3. **Short live proof.** Only when the change touches launch, the HUD clock, hero selection, the end screen, OBS scenes, download pacing, or predictions. One replay until the clock reads `MM:SS` and a hero is selected (`1`–`0`), then `services stop`. Streaming stays off on ASA-SERVER unless the proof is about the stream (see Environments).
4. **Long proof, 1 to 5 games.** Only when the user asks to prove the loop, or the change is end-of-match, the next replay loading, request-before-Standard, or a prediction opening and resolving. Each counted game reaches the core (HUD time within a second or two of core death) and the next replay starts loading. Do not kill the stack mid-game to rebuild. Do not continue past five. Then `services stop`, and confirm Heroes of the Storm is gone.

### Current patch and previous patch

Both of these are acceptance criteria for a local end-to-end run. A run that only proves one of them is not done.

The newest installed `Versions\Base*\HeroesOfTheStorm_x64.exe` is the current patch. An older exe in that folder is a previous patch. A `Base*` folder with no executable is not installed (an empty `Base98297` does not count); an older build that is not installed is downloaded through HeroesSwitcher (Missing build, below). `ReplayClientRoute` chooses the launch. The running exe file version must equal the replay version before the match clock counts.

- **Current patch.** Ask the logged-in Battle.net to launch Hero (`--exec="launch Hero"`, which is `-sso=1`). Do not open the `.StormReplay` first. A direct HeroesSwitcher open starts the game without SSO and stops on the email/password form. Wait for the signed-in home screen, then open the replay. A login form means that process was not signed in: close it and ask Battle.net again once. Do not type credentials. Do not click Update. A matching client already in a match (a match in memory after its menu, or a running memory clock) is the replay the report preloaded: the session starts there. A Wait is bounded: when the matching client shows no menu, loading screen, match, match clock, game data, or blank startup window for `Spectate:LaunchWaitLimit` (3 min), it is closed and Battle.net is asked again, once (#249).
- **Battle.net agents.** Each `--exec` leaves a new `%ProgramData%\Battle.net\Agent\...\Agent.exe` that never exits (#251). `BattleNetAgentReaper` runs when spectate starts and before each Battle.net launch: it keeps Battle.net's own agent (the oldest one a running `Battle.net.exe` started at least the grace period ago, else the oldest) and terminates every other `Agent.exe` under that folder older than `BattleNetAgents:GracePeriod` (2 min), plus a `conhost.exe` a reaped agent started. It never touches anything else. `BattleNetAgents:Enabled` (true) turns it off; more than `WarnAboveCount` (5) agents logs a warning. `services status` and the supervisor report the counts (`MachineHealth`, `docs/service-split.md`).
- **Previous patch.** Do not ask Battle.net to launch Hero and do not click Update. Play always starts the newest client. This client does not log in, so it reads the root `Documents\Heroes of the Storm\Variables.txt`, not the account file. `replayinterface` and `observerinterface` must be `AhliObs 0.75.StormInterface` before that process starts. That is the interface file name. The dropdown label `AhliObs 0.75` does not name the file, and the client loads the default HUD. Open the `.StormReplay` through HeroesSwitcher, the same open Explorer uses, so it starts that older `Versions\Base*` exe. If another build is already running, leave it. The switcher starts the newest exe first, then switches to the replay's build, and that handoff is not closed. A login form on that older exe is not the signed-in session: close it and open the replay through the switcher once more. The older client may load the replay without a home screen. A loading screen or match clock on the matching exe is success. If it does stop on the home screen, open the replay from there. When that matching exe is already up without the home screen or the match clock, open the replay through HeroesSwitcher and do not close the client. If it still shows nothing usable after `Spectate:LaunchWaitLimit`, the replay goes through HeroesSwitcher once more, and the client stays open. A download dialog, "Preparing game data", or a blank full-size window on that process is startup. Leave it running until the data download finishes.
- **Missing build.** When the replay's `Versions\Base<build>` exe is missing and is older than the current patch, Blizzard downloads that old client by itself, as long as it still serves that build. Reclaim comes first (below); with no retained copy, open the `.StormReplay` through HeroesSwitcher exactly like a previous patch (`ReplayClientPatch.Download`). Proven on ASA-SERVER on 2026-10-07: `Base98285` was an empty folder with no copy in `Data\Clients`, and replay 65550003 (2.57.0.98285) was opened with `HeroesSwitcher_x64.exe "<replay>.StormReplay"` at 14:42:35. The switcher started the newest exe (`Base98348`) first. At 14:43:16 `Versions\Base98285\HeroesOfTheStorm_x64.exe` (52 MB) appeared, the switcher ran again and handed off to it, and at 14:44:31 `heroesreplay check timer` read the memory match clock at 00:00:34 on that exe. About 2 minutes, no clicks. The newest-exe handoff and the download are startup: do not close them, do not reopen the file, and do not treat a login form on that handoff as a failed sign-in. The launch waits up to `Spectate:BuildDownloadLimit` (10 min) for the exe to appear, and the cold-boot deadline runs from when it does. After that the previous-patch rules apply, and a loading screen or memory match clock on that exe is success. When Blizzard does not serve the build (the exe never appears within the limit, or the version-mismatch dialog shows before it does; for 2.57.0.98297 on 2026-10-07 the newest exe showed "The version of Heroes of the Storm required to play this game is not available.", which counts as that dialog), Heroes is closed, the replay is deferred as `BuildNotInstalled` (inconclusive for the release health gate), and the build is written to `Data\client-download-holds.json`. For `Spectate:BuildDownloadHold` (4 h) the launch, the spectate queue, and `heroesprofile download` treat that build as not installed, so the download is not retried on every replay. A missing build newer than the current patch is a Battle.net update that has not happened: do not launch anything and do not click Update. That replay stays queued, and a Heroes process that is already running is not closed.
- **Patch line.** A main patch is the first two numbers (`2.57.*`). Every build iteration of that line is its own client: `2.57.0.98285`, `2.57.0.98304`, and later `2.57.*` builds. They are not interchangeable. The replay build must equal the exe. The newest installed exe is still the current patch and signs in through Battle.net. Each older iteration is a previous patch and opens through HeroesSwitcher. `heroesprofile patch-index` reports the first replay id of that whole line, not only the newest build number.
- **Reclaim.** Battle.net deletes the previous iteration's `Versions\Base*` exe while it installs the next one. An empty `Base*` folder is still not a client. Before a replay launches, copy each supported exe into `Location:RetainedClientDirectory` (`Data\Clients` when that is empty). When the live exe for the replay's build is missing and a copy exists, copy it back into `Versions\Base*` before HeroesSwitcher runs. A copy is faster and does not depend on Blizzard, so it always comes before a download. A build deleted before any copy was kept is fetched through HeroesSwitcher (Missing build, above). Do not click Update or Play to fetch it. A build Blizzard downloaded is an installed build from then on, so the next replay's launch copies it into the retained folder. The retained set is every installed build on the `MinimumGameVersion` patch line, plus any installed build at or above that floor.
- **Firewall.** Windows asks to allow public and private networks the first time each `Versions\Base*\HeroesOfTheStorm_x64.exe` listens. Before Battle.net or HeroesSwitcher starts Heroes, the spectator reads the inbound rules (`netsh advfirewall firewall show rule name=all dir=in verbose`) and checks for an allow rule for every installed client exe. Any enabled inbound Allow rule for that exe counts, whatever its name, including the `Query User{...}` rules Windows adds when someone answers its prompt (netsh shows them as "Heroes of the Storm", with the path in lower case). Paths compare case-insensitively after environment variables are expanded and the path is made full. A disabled, Block, or outbound rule does not count. A rule for only some profiles still counts, and the log names its profiles (#284). Do not click Allow. The rule is for that exe path, so a new `Base*` folder needs its own rule, including one Blizzard downloaded during a launch (it has no rule until `client firewall` runs). Adding a rule is the only step that needs administrator rights: run `heroesreplay client firewall` once from an elevated shell after a new client build is installed. It adds the named `HeroesReplay inbound Base<build>` rule only for an exe that no Allow rule covers, so it never duplicates Windows' rule. Spectate itself runs unelevated (issue #133) and warns about a missing rule once per exe per process, not on every launch.
- **New client.** When a newer `Base*` exe is installed, write windowed 1080p and AhliObs into root and account `Variables.txt` and copy the interface again before that client starts, even if the files already name AhliObs. Do this only while Heroes is not running. Do not close a client HeroesSwitcher launched in order to reload AhliObs or to pass the replay a second time. HeroesSwitcher opens the newest exe first so it can start an older build. A download or preparing screen on that newer exe is the handoff and stays up. A blank full-size window on the process the switcher started is still startup and stays up. Text that already names AhliObs is not the HUD. A HeroesSwitcher process with no Heroes process is closed before the next open. The download screen is stock chrome. Do not click Play, Update, or Allow.

A version-mismatch or region-unavailable dialog leaves the front on the first miss. That dialog is an invalid client, so Heroes closes and the next replay can start. A client that is still downloading data, or still switching to the replay's build, is not that dialog and is not closed. The replay is eligible again after the defer interval. Battle.net is not clicked. On ASA-SERVER the long proof includes one current-patch replay and one previous-patch replay whenever both clients are installed. Each must reach the match clock on its own exe. Test uploads stay private and titled with `[TEST]`. Do not set `HEROES_REPLAY_ENV=prod`. Start Twitch ingest only when the proof is about the stream; it goes to the developer Twitch account (see Environments).

Live-proof logs: Aspire dashboard at `http://127.0.0.1:18888`. The Aspire CLI on this machine is 13.5.4 and its MCP server is `aspire agent mcp`. This app has no AppHost, so connect it in dashboard-only mode: `aspire agent mcp --dashboard-url http://127.0.0.1:18888`. That mode exposes only `list_structured_logs`, `list_traces`, and `list_trace_structured_logs`. `list_resources` and `execute_resource_command` need an AppHost and are not available here. If that MCP is not connected, use `aspire otel logs`. Spectate, Twitch, download, and YouTube. No unhandled error stacks. No repeated invalid-timer flood. Service consoles must not cover the clock pill.

Role logs do not need Aspire. Each role that `services start` launches writes `%LOCALAPPDATA%\HeroesReplay\logs\<role>-<yyyy-MM-dd>.log` (Information and up, tokens redacted, 20 MB parts, kept 14 days; `ServiceLogs`), and the supervisor writes `supervisor-<date>.log` there. `services status` prints each role's file. Read it first when a role is failed, degraded, or was restarted.

### Supervision

`services start --supervise` keeps its console as the supervisor once the roles are ready (`services supervise` attaches to a stack that is already running). It restarts a failed role after 10 s, 30 s, 2 min, then 5 min, kills and restarts a role whose heartbeat is 2 min old, and after 5 restarts in 30 min leaves the role down with one error and `service.restart_budget_exhausted` in `services status`. Degraded roles are not restarted (including a role whose own dependency probe found its key, token, or service rejected or unreachable, #305; `docs/service-split.md` Dependency probes), with one exception: spectate degraded with `causeCode` `spectate.launch_stalled`, when one replay's launch and loading phase went `ServiceHealth:SpectateLaunchStallThreshold` (20 min) without match progress. The supervisor logs a WRN, kills it, and it restarts like a failed role while the budget has room. The report, a hold, an empty queue, and an outage are not that phase, and a client downloading or preparing game data starts it over (#249). Spectate work is match progress only: the clock advanced, or a session reached the clock or the award screen. A deferred, held, or timed-out replay is not work, and 3 such sessions in a row (`ServiceHealth:SpectateNoProgressSessions`) make spectate degraded with `causeCode` `spectate.no_match_progress` and the last outcome. `services stop` stops the supervisor before the roles, so nothing restarts during a stop. One supervisor per machine: `services start` and `services supervise` refuse while one runs in any session, including over SSH (#293). Rules: `ServiceRestart`; details: `docs/service-split.md`.

- **DESKTOP-8SJEK72:** run the stack supervised. In a scheduled downtime, `services stop`, then `heroesreplay services install-task --environment prod` once from `C:\heroesreplay\app` (no administrator rights) and `schtasks /Run /TN HeroesReplay-live`. The task starts `services start --supervise` in its own console window at every logon; leave that console open. Release updates restart through the task and always leave the stack supervised. Closing the console ends supervision only; `heroesreplay services supervise` attaches again. Do not kill the supervisor to stop the stack; use `services stop`, which fails while OBS runs with a websocket that does not answer, because the stream may still be live.
- **Spectate down for good.** When spectate uses its restart budget, the supervisor makes a live stream safe (`ServiceRestart:SpectateDownObs`: `WaitingScene` by default, `StopStream`, or `None`).
- **ASA-SERVER:** prove supervision with `HEROES_REPLAY_ENV=dev` and `services start --supervise --roles download,youtube` only. Not spectate, not `twitch connect`. Kill a role with `Stop-Process -Force` to see a restart. End with `services stop`, then confirm `tasklist` shows no `heroesreplay.exe` and no `services.json`, `services.stop`, or `supervisor.json` is left in `%LOCALAPPDATA%\HeroesReplay`.

On ASA-SERVER, after a spectator, OCR, OBS, or Twitch change: `services stop` (this closes HotS), `dotnet build heroes-replay.slnx -c Release` (the build copies `src/HeroesReplay.CLI/appsettings.secrets.json` into the Release bin), and start a proof only if phase 3 or 4 applies. Twitch ingest here goes to the developer account, so start it only for a stream proof.

### Capture

`IGameCapture` is the capture port. `PrintWindowCapture` is the default (`Capture:Method` = `PrintWindow`): the game HWND's composed frame, cropped to the client area, including DirectX. Another window on top of the game does not replace the clock. `BitBltCapture` is the desktop copy and is only used when `Capture:Method` is `BitBlt`. Windowed mode is still required so DWM has a frame.

OBS game capture also sees the frame when the window is covered, because it hooks the swap chain. Issue 27 (closed) timed five `GetSourceScreenshot` calls one second apart at 37–72 ms each. OBS is not the clock.

The match clock is memory only. `StableMatchClock` (HeroesClientSDK) is read-only; `StableGameTimer` wraps it as `IGameTimer`, and `GameTimerLog` logs it. It finds the tick global from the clock instruction pattern on each client build (`MatchClockPattern`). Build `2.55.17.98025` also has fixed addresses (`MatchTickClock`, seconds = ticks / 4096) as a candidate. The HUD timer is never cropped or OCR'd, and there is no screen fallback: a read that is not ok means the match has not started, is between matches, or is over. Hero selection starts on the first ok read. The launch wait, the recording start, and the next-match handoff count a match as running only when two reads 250 ms apart move forward (`StableMatchClock.IsRunning`), so the menu's zero or the last match's frozen clock never counts. A fresh cell is confirmed when a second read moves forward by no more than the wall time between the reads plus 8 s, and a probe that is still confirming reads once more, so a slow caller (a launch pass with OCR on a hung window) still locks a relaunched client's clock (#249). Every memory reader starts over on a new client process, keyed by pid and process start time. `heroesreplay check timer` reads the same memory clock.

The map loading screen is memory first too (`LoadingScreenMemory` in HeroesClientSDK, #168; `ReplayLoadCue` and `HomeScreenCue` here decide what its samples mean). `LoadingScreenPattern` finds the client's screen-state global per build: the global that two `mov rcx,[G]` loads agree on in the most `test rcx,rcx; jz; xor edx,edx; call; test al,al; jz; mov rcx,[G]; call` sites (RVA `0x3771830` with 34 sites on `2.57.0.98304`). `[[G]+0x218]` is the screen object: null in a match, otherwise bit 0 of byte 72 is 1 on a loading screen and 0 on a menu. A match after that process's menu is the replay on screen, even before the clock reads (#249). The boot splash is a loading screen too, so a loading screen counts only after that client process has shown a menu. Memory decides whenever it can tell; OCR of `OCR:LoadingScreenText` ("WELCOME TO", the map, players, heroes) is read only when it cannot (pattern not found yet, or no menu yet, as on a previous-patch client that loads the replay without a home screen). The home screen is memory first as well (`HomeScreenCue`): a menu in memory is home unless OCR reads a login form, and a loading screen or match in memory is not home, so a Heroes window that captures black (not in front) still opens the next replay. OCR still reads the login and download dialogs and the end screen. A failed pattern scan (a client still unpacking its code) is retried every 10 s, for the clock too. Shadow mode (#292) logs the memory verdict next to every OCR screen verdict (`ScreenShadow`, `ScreenMemoryVerdicts`, `OCR:ShadowEnabled`, on by default) and counts `heroesreplay.screen.shadow` by `state` and `verdict` (agree, disagree, memory_unknown). A disagreement warns at most once per state per minute and saves a fresh frame at most every 5 min per state under the replay context's `shadow` folder; it changes no decision, and OCR still decides wherever it did.

## Environments

The environment variable and its appsettings overlay decide behavior. Code never compares the machine name: Twitch ingest follows `OBS:StreamingEnabled`, and the YouTube title marker, privacy, and publication budgets follow `YouTube:TitlePrefix` and `YouTube:PrivacyStatus`. The hostnames below only tell an agent which box it is on.

Ingest also needs a machine-local arm, `%LOCALAPPDATA%\HeroesReplay\stream-armed` (`heroesreplay obs arm` / `obs disarm` / `obs status`). It is not in the repo or the release zip, and no setting can move it, so the overlay alone cannot start production ingest. The live box is armed. ASA-SERVER may be armed for a stream proof: its OBS streams to a developer Twitch account, not `saltysadism`, and `appsettings.dev.json` leaves `OBS:StreamingEnabled` false, so it streams only in a run that sets `HEROES_REPLAY_OBS__StreamingEnabled=true`. Recording does not need the arm.

| | **dev** | **live** |
| --- | --- | --- |
| Config | `HEROES_REPLAY_ENV=dev` (`appsettings.dev.json`) | `HEROES_REPLAY_ENV=prod` (`appsettings.prod.json`) |
| Hostname | `ASA-SERVER` | `DESKTOP-8SJEK72` |
| Role | Develop, prove, and harden the spectator, CLI, and services. Not a broadcast: its OBS streams to a developer Twitch account. | Real spectating and the 24/7 Twitch stream (`saltysadism`). Intel Arc A310 guest vs this live box. |
| Repo | `C:\heroesreplay\HeroesReplay` | Same path. A release install rewrites OBS assets under its own `obs` folder (`C:\heroesreplay\app\obs`), not this checkout. |
| Stream | Test streams are allowed. OBS here streams to a developer Twitch account, not `saltysadism`, so going live does not interrupt the live channel. To prove the stream path (for example the waiting scene held through a release restart), run `obs arm` and set `HEROES_REPLAY_OBS__StreamingEnabled=true` for that run; `services stop` ends the stream. If this box's OBS stream service changes, confirm it is still the developer account before going live. OBS recording and YouTube are for testing only (private, `[TEST]`, dry-run). The Twitch API settings here still name the live channel (`Twitch:Account` and `Twitch:Channel` are `saltysadism`), so `appsettings.dev.json` turns every Twitch side effect off: predictions, chat (`EnableChatBot`), channel-point redemptions and reward sync (`EnablePubSub`, `EnableRequests`), with `DryRunMode` on (#126, #146). | Production ingest (`OBS:StreamingEnabled` plus the machine arm). Do not experiment on the live stream. |
| Upgrades | Safe to stop spectate, rebuild, reboot the guest (not Unraid/Tower). | Schedule **downtime** before pull, rebuild, client/OBS upgrades, or reboots. |
| Spectator engine | **Normal** to kill `heroesreplay`, quit HotS, rebuild Release, and relaunch only long enough to prove a change. Stop when the proof is done. | Do **not** kill/rebuild/restart the spectator as a routine. This is the production spectate. |

On **ASA-SERVER**, prove a change with a short run, then read `%LOCALAPPDATA%\HeroesReplay\status.json` and `Data\Contexts\<id>\end.png` if the client was closed. Do not keep spectating after that. On **DESKTOP-8SJEK72**, ask before stopping anything.

### Maps, modes, client

- Loop: Storm League, current patch. Rewards: QM, SL, ARAM. Not Unranked Draft (removed from the client) or brawls (`Maps:Catalog` `Playable: false`).
- Ranked/QM maps: Infernal Shrines, Sky Temple, Cursed Hollow, Dragon Shire, Towers of Doom, Tomb of the Spider Queen, Volskaya Foundry, Garden of Terror, Blackheart's Bay, Warhead Junction, Alterac Pass, Battlefield of Eternity, Hanamura Temple, Haunted Mines, Braxis Holdout.
- ARAM: Silver City, Lost Cavern, Industrial District, Braxis Outpost.
- ReplayId rewards: the current patch line (`MinimumGameVersion` and every newer build, including each `2.57.*` iteration). The build's exe does not have to be installed: an older build is downloaded through HeroesSwitcher when the replay plays. A build Blizzard no longer serves ends `BuildNotInstalled` and the redemption stays unfulfilled while the replay is held.
- Spectator keys: `1`–`0` observe player. Do not send `C` (follow player camera) or Shift+Z ultra zoom.
- Twitch: chat and EventSub redemptions reconnect with backoff; Helix predictions; channel-point rewards queue `Data\requests.json`. Reward prompts must say recent patch ReplayIds.

### Calculators

`Calculators:Enabled` in appsettings (type name, default on) for A/B. Example: `"EmotingCalculator": false`.

### Directory tree

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
| `C:\heroesreplay\Data\HeroesData` | heroes-data2 JSON cache (Heroes.Element). Downloaded from HeroesToolChest/heroes-data2 when that cache is missing |
| `C:\heroesreplay\secrets` | Local backup of gitignored `appsettings.secrets.json` |
| `C:\heroesreplay\tools\ffmpeg` | `ffmpeg.exe` and `ffprobe.exe` for clips, from `heroesreplay deps install` (`Dependencies:Directory`). `apply-release.ps1` runs it after every install. Never set up by hand |
| `%USERPROFILE%\Documents\Heroes of the Storm\Interfaces` | AhliObs (`client configure`) |
| `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` | OBS collection from `obs/Default.json` (`OBS:SceneCollectionName`) |
| `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\basic.ini` | OBS profile (`OBS:ProfileName`). Machine-owned: `obs/Default/basic.ini` is copied only when it does not exist, and updates keep it |
| `%LOCALAPPDATA%\HeroesReplay\stream-armed` | Machine-local Twitch ingest arm. The live box, and ASA-SERVER for stream proofs (developer Twitch account) |
| `%LOCALAPPDATA%\HeroesReplay\obs` | `backups\` (the live collection or profile before each write, newest 10), `managed-collections.json` (the template each live collection was written from), `release-rollback.json` (which backup a rollback of the last release puts back) and `restore-pending.json` (a rollback that waits for OBS; `services status` shows it). A custom collection is never overwritten. See `docs/obs-operations.md` |

`Location:DataDirectory` is `C:\heroesreplay\Data`. Contexts are `Data\Contexts` (not a sibling of Data). Do not copy OBS `service.json` (stream key). The OBS profile and scene collection names are `OBS:ProfileName` and `OBS:SceneCollectionName` (default `HeroesReplay`); a stream or recording does not start while OBS has another one active.

Detect with `hostname`. If `DESKTOP-8SJEK72`, ask before stopping `heroesreplay` / HotS / OBS, and do not start a Twitch stream from a test build. If `ASA-SERVER`, do not SSH to Unraid (`Tower` / 192.168.1.102), do not bind the host 3090/iGPU, and do not reboot Tower.

New machine: clone into `C:\heroesreplay\HeroesReplay`, then `pwsh -File tools/bootstrap-workstation.ps1` (skill `op-service-account`). It also fetches the pinned HeroesClientSDK package into `.packages` (`tools/restore-sdk-package.ps1`, no credentials).

## Hard rules

- Target `net10.0-windows10.0.19041.0` for CLI/Core/Tests. WinRT OCR and the PrintWindow/BitBlt capture need the Windows TFM.
- File-scoped namespaces, usings outside the namespace, `using` declarations where they reduce nesting.
- Calculators implement `IFocusCalculator.Contribute(ReplayTimeline)`. Do not bring back `GetFocusPlayers(TimeSpan, Replay)` × PLINQ.
- Focus pipeline: parse replay → calculators offer weighted events → hold last winner across empty seconds → spectator looks up by the memory match clock until core kill. Do not recompute calculators in the live loop.
- Living units: `unit.IsAliveAt(now)` (`TimeSpanDied == null` means alive).
- OBS: websocket **5**, port **4455**, **one connection per replay** (`BeginSession` / `EndSession`). Do not connect-disconnect per request. The read-only MCP OBS tools are a separate client: one short session per tool call, Get requests only.
- Cache the Heroes of the Storm HWND after launch; do not `GetProcessesByName` on every keystroke.
- `spectate file` plays the queue **once**. Heroes Profile provider loops.
- Never commit `appsettings.secrets.json`, user `*.StormReplay` dumps, or large `*.mp4`.
- Package versions live in `Directory.Packages.props`. Do not pin .NET 11 / CommandLine 3 prereleases. `HeroesClientSDK` is an exact pin (`[$(HeroesClientSDKVersion)]`) with its SHA-256 beside it, restored from `.packages`; never a floating range.
- Heroes Profile HTTP retries use `Microsoft.Extensions.Http.Resilience` on the Kiota `HttpClient`. Other retries use `Microsoft.Extensions.Resilience` pipelines. Replay cache is `IMemoryCache`. Do not add a direct Polly package reference.

## Verification

- Analysis changes: `dotnet test` plus `calculators coordinates` when replay format is in play.
- OBS changes: `check obs`; keep scene/source names configurable; wait for `IsIdentified`.
- Twitch: `check twitch`. PubSub reward topics are obsolete; do not add new ListenToRewards usage.
- CLI command changes: Smoke tests + `--help` on the new command.
- Clips and ffmpeg: `check ffmpeg`. External tools are installed by `heroesreplay deps install` from `src/HeroesReplay.Core/Dependencies/dependencies.json`, never by hand. Unit tests use a fake download; CI does not download ffmpeg.

## Repo skills (this tree)

| Skill | Use when |
| --- | --- |
| `.agents/skills/heroes-replay-cli` | spectate, check, calculators, secrets, `op://` |
| `.agents/skills/op-service-account` | `OP_SERVICE_ACCOUNT`, fill secrets on a new clone |
| `.agents/skills/dotnet-10-csharpier` | SDK, slnx, CSharpier, TFM, test categories |
| `.agents/skills/obs-websocket-v5` | OBS Studio control, scenes, recording, `check obs` |
| `.agents/skills/twitch-integration` | TwitchLib, rewards, predictions, `check twitch` |
| `.agents/skills/ffmpeg` | Cut pentakill clips from OBS recordings, full 1920x1080 frame. ffmpeg 9.0.2 is pinned (version, URL, SHA-256) in `src/HeroesReplay.Core/Dependencies/dependencies.json` only; `heroesreplay deps install` puts it in `C:\heroesreplay\tools\ffmpeg`, and `check ffmpeg` reports what clips will run. |
| `.agents/skills/release-install` | Install production from the GitHub Release zip instead of cloning and building. |

Slash: `/heroes-replay-cli`, `/op-service-account`, `/dotnet-10-csharpier`, `/obs-websocket-v5`, `/twitch-integration`, `/ffmpeg`, `/release-install`. `csharp-solid` is also in this folder.

## Official .NET skills

These are installed under `.agents/skills/<name>/` from [dotnet/skills](https://github.com/dotnet/skills) commit `e115891bd2ac` (MIT, `.agents/skills/dotnet-skills.LICENSE.txt`). Load the skill whose name matches the task. Each `SKILL.md` is the source of truth for when to use it. `.agents/skills/vendored.json` pins the source commit, the license file, and the list; `AgentDocsTests` fails when a folder under `.agents/skills` is neither in it nor a first-party skill in the table above, or when the CLI skill misses a command.

| Plugin | Skills |
| --- | --- |
| `dotnet` | `csharp-refactoring`, `setup-local-sdk` |
| `dotnet-upgrade` | `dotnet-aot-compat`, `migrate-dotnet10-to-dotnet11`, `migrate-nullable-references` |
| `dotnet-msbuild` | `binlog-failure-analysis`, `binlog-generation`, `build-parallelism`, `build-perf-baseline`, `build-perf-diagnostics`, `check-bin-obj-clash`, `copy-to-output-directory`, `directory-build-organization`, `eval-performance`, `extension-points`, `including-generated-files`, `incremental-build`, `item-management`, `msbuild-antipatterns`, `msbuild-modernization`, `property-patterns`, `resolve-project-references`, `target-authoring` |
| `dotnet-nuget` | `convert-to-cpm` |
| `dotnet-test` | `assertion-quality`, `code-testing-agent`, `code-testing-extensions`, `coverage-analysis`, `crap-score`, `detect-static-dependencies`, `filter-syntax`, `find-untested-sources`, `generate-testability-wrappers`, `grade-tests`, `migrate-static-to-wrapper`, `mtp-hot-reload`, `platform-detection`, `run-tests`, `scaffold-dotnet-test-project`, `test-analysis-extensions`, `test-anti-patterns`, `test-gap-analysis`, `test-smell-detection`, `test-tagging`, `testability-obstacle` |
| `dotnet-diag` | `analyzing-dotnet-performance`, `clr-activation-debugging`, `dotnet-trace-collect`, `dump-collect`, `microbenchmarking` |

Not vendored because they do not apply to this repo: `migrate-dotnet8-to-dotnet9` and `migrate-dotnet9-to-dotnet10` (already on .NET 10), `thread-abort-migration` (no .NET Framework code), `writing-mstest-tests` (tests use xUnit), and `android-tombstone-symbolication` and `apple-crash-symbolication` (Windows only). Some vendored skills still point to `writing-mstest-tests` for MSTest work. Ignore those pointers.

Grok already provides `review`, `create-skill`, and `long-running-background-tasks`. They stay with the tool and are not copied into this repo.

There is no high-quality public skill specifically for **obs-websocket 5** or **TwitchLib 3.x** — that is why the two repo skills exist. Channel-point redemptions arrive over Twitch EventSub (`EventSubRewardListener`), not PubSub.
