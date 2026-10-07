# Service split

Design for [#14](https://github.com/HeroesReplay/HeroesReplay/issues/14). Merged to `develop` and `master`. The CLI orchestrates processes. It does not host long-running loops in its own process.

## What one process does today

`Program` only builds `CommandLineService` and parses args. Each long-running command then builds a service provider and blocks inside `heroesreplay.exe`:

| Entry | What it hosts |
| --- | --- |
| `spectate file` / `spectate heroesprofile` | `AddSpectateServices` then `Engine.RunAsync`. Heroes Profile mode plays the local cache only. |
| `twitch connect` | `AddTwitchServices`: chat, EventSub channel-point redemptions, and a status-file prediction watcher. No game, OCR, or OBS graph. |
| `heroesprofile download` | List and download into `Data\Standard` and `Data\Requests`. No spectate loop. |
| `youtube uploader` | `AddYouTubeServices` then `YouTubeUploader.ListenAsync` |
| `mcp` | Already a second process (stdio). Leave it that way |

`Engine.RunAsync` loads game data, then `Task.WhenAll` of two loops in that process:

- Spectator: `IReplayProvider.TryLoadNextReplayAsync` then `GameManager.LaunchAndSpectate`
- Connectivity watchdog (writes `status.json`, optional OBS)

`TwitchBot` is not started by `Engine`. Chat, rewards, and predictions belong to `twitch connect`.

`GameManager` configures the Storm client, launches Heroes of the Storm, sends keys through `GameWindowInput`, runs `Spectator` (match clock from game memory only; WinRT `OcrEngine` on an `IGameCapture` frame reads the home, loading, and end screens, never the timer), opens one OBS websocket session per replay, then kills the game. It does not call Helix. When the session ends it writes `completedReplayId`, `completedAt`, and `completedWinnerTeam` on `status.json` for the Twitch process. `heroesprofile download` lists and saves replays. `spectate heroesprofile` only plays files already on disk (`ReplayCacheProvider`). `ReplayFileProvider` plays a local queue once and does not loop the API.

`youtube uploader` is not inside `Engine`, but it is still an in-process service of the CLI. It watches `Data\Contexts` for `*.mp4` plus the YouTube entry json.

## Process boundaries

Shared state stays on disk. No message bus in v1.

| Process | Owns | Must not own |
| --- | --- | --- |
| CLI | Short commands: `services start` / `services stop` / `services status`, plus today's one-shot `check`, `client`, `calculators`, reward admin, `mcp` | `Engine.RunAsync`, `TwitchBot.InitializeAsync`, `YouTubeUploader.ListenAsync`, the Heroes Profile poll loop |
| Spectator (Windows only) | HotS process, window input, WinRT OCR / frame capture, focus loop, per-replay OBS session, connectivity watchdog, `%LOCALAPPDATA%\HeroesReplay\status.json` (including match completion fields) | Twitch sockets, Helix predictions, Heroes Profile list/download, YouTube upload |
| Twitch | Chat, EventSub redemptions, reward handlers writing `Data\requests.json`, predictions read from `status.json` / context | Game HWND, OCR, OBS, replay parse |
| Heroes Profile downloader | List and download into `Data\Standard`; fulfill requests into `Data\Requests` | Spectating, Twitch, YouTube |
| YouTube uploader | Existing watcher: context `*.mp4` + entry json, OAuth upload | Spectating, Twitch, download |

The spectator pieces stay one process. They share the cached HWND, the capture surface, and the one OBS connection per replay (`BeginSession` / `EndSession`). Splitting input, OCR, and OBS across processes would pass window handles around for no gain.

Twitch, the downloader, and YouTube do not need the game. They can be separate Windows processes now and containers later, with `C:\heroesreplay\Data` (and secret files, not printed tokens) mounted. The game, `GameWindowInput`, and WinRT OCR cannot leave the Windows game box. OBS stays with the spectator because the session is per replay.

`CaptureMethod.None` keeps `FakeTwitchBot` / `StubController` for local runs. That stub is not a second deployable service.

## Contracts already on disk

- `%LOCALAPPDATA%\HeroesReplay\status.json` — phase, timer, map, replay id, core death, and when the session ends `completedReplayId`, `completedAt`, and `completedWinnerTeam`. Twitch predictions and `status` read this. They do not call into the spectator.
- `Data\requests.json` (and the failed file) — Twitch enqueues; the downloader fulfills; the spectator only sees local `.StormReplay` files. Both processes lock the files with a named mutex.
- `%LOCALAPPDATA%\HeroesReplay\panel-requests.json` — `!talents` and `!stats` from `twitch connect`. The spectator consumes the pending panel and sends the hotkey.
- `Data\Standard` and `Data\Requests` — replay cache. `spectate heroesprofile` plays that cache (same shape as `spectate file`).
- `Data\Requests\<replay>.request.json` — the redemption of a requested replay. The downloader writes it before the replay file appears. The spectator links the session to it by replay id, whichever folder or path loaded the file (#165).
- `Data\redemption-dispositions.txt` — the spectator appends a line when a requested match is verified. `twitch connect` marks that redemption FULFILLED on Twitch and records it in `Data\redemption-fulfilled.txt`. A session that was not verified writes nothing: the redemption stays UNFULFILLED and the replay plays again (#169).
- `Data\Contexts\<id>\` — recording, end screenshot, YouTube entry. The uploader already keys off these files.

## Order of work

1. This document only.
2. Done: `Engine` no longer starts `TwitchBot`. `twitch connect` uses `AddTwitchServices` and does not build the game/OCR graph.
3. Done: `heroesprofile download` lists and downloads. `spectate heroesprofile` uses `ReplayCacheProvider` and only plays files already in `Data\Standard` and `Data\Requests`. Existing files are seeded into `Data\spectated-ids.txt` so the cache is not replayed from the beginning.
4. Done: Blue/Red predictions run in `twitch connect`. The spectator does not call Helix. `twitch connect` opens a prediction when `status.json` phase is `TimerDetected`, and settles it from `completedReplayId` / `completedAt` / `completedWinnerTeam` (0 blue, 1 red, null cancels). Those completion fields are written when the spectate session ends and are not cleared when the next replay loads.
5. Done: `heroesreplay services start` launches four processes (`spectate heroesprofile`, `twitch connect`, `heroesprofile download`, `youtube uploader`) and records their pids in `%LOCALAPPDATA%\HeroesReplay\services.json`. Each process is detached in its own console window. Its pid file and, since #153, its rolling log file go to `%LOCALAPPDATA%\HeroesReplay\logs\` (see [Role logs](#role-logs)). `services stop` writes `%LOCALAPPDATA%\HeroesReplay\services.stop`. Spectate, `twitch connect`, `heroesprofile download`, and `youtube uploader` cancel on that file. The spectator then runs its normal shutdown, which closes Heroes of the Storm. Processes still alive after 20 seconds are killed, and Heroes of the Storm is closed if spectate was one of the recorded processes. Once every role has exited, stop reads OBS `GetStreamStatus` once without changing it. It exits 1 when a role is still running (that role stays in `services.json`), the game is still open, or OBS is still streaming. Start does not turn on Twitch ingest, and it clears a leftover stop file before launching.
6. Done: `!talents` / `!stats` and `Data\requests.json` are shared files with a cross-process lock. Chat in `twitch connect` can show a panel in the spectator process.
7. Optional later: Windows services or containers for Twitch, the downloader, and YouTube. Not for the spectator.
8. Done (#149): continuous role health. See below.
9. Done (#153): durable per-role logs and an opt-in supervisor with a bounded restart policy. See below. Windows service installation is a later slice of #130.

## Role health

Each role writes its ready file, `%LOCALAPPDATA%\HeroesReplay\ready\<nonce>.json`, when it is ready, then rewrites it every `ServiceHealth:HeartbeatInterval` (15 s) from a timer (`ServiceHeartbeat`). The file carries `role`, `version`, `executablePath`, `nonce`, `pid`, `readiness` (`ready`, `stopping` once the stop file or Ctrl+C reaches the role, `exited` when it left its loop without one), `readyAt`, `heartbeatAt`, `heartbeatIntervalSeconds`, `lastSuccessfulWorkAt`, and `lastError` (`message`, `at`). Spectate also writes `sessionsWithoutProgress`, `lastOutcome`, `launchingSince`, and `sessionOutcomes` (sessions this process ended, by outcome; the release health gate reads it). Error and critical logs become `lastError`, with tokens redacted.

Successful work is role-defined:

| Role | Work | Default `ServiceHealth` threshold |
| --- | --- | --- |
| spectate | Match progress: the match clock advanced, or a replay session reached the clock or the award screen. A deferred, held, timed-out, or failed session, an idle wait, and an outage pause are not work | `SpectateWorkThreshold` 20 min |
| twitch | A Twitch reconcile: one prediction watcher pass (every second) | `TwitchWorkThreshold` 5 min |
| download | A download pass (every 2 to 15 s) | `DownloadWorkThreshold` 15 min |
| youtube | An upload pass: the pending drain, or the one-minute poll | `YouTubeWorkThreshold` 30 min |

`services status` reads `services.json`, the process table, and each heartbeat:

| State | Code | Rule |
| --- | --- | --- |
| ready | `service.ready` | Alive, heartbeat fresh, work inside the threshold, no newer error |
| degraded | `service.degraded` | Alive and heartbeating, but the last successful work (or `readyAt` before the first) is older than the role's threshold, or `lastError` is newer than it. Spectate is also degraded, with `causeCode` `spectate.no_match_progress` and the last outcome in the cause, once `SpectateNoProgressSessions` (3) replay sessions in a row end without match progress. Spectate is degraded with `causeCode` `spectate.launch_stalled` when one replay's launch and loading phase (`launchingSince`) is older than `SpectateLaunchStallThreshold` (20 min) with no match progress. That phase starts when a session launches a replay and ends on match progress, at the report, and when the session ends; game data still downloading or preparing starts it over. An empty queue, an outage pause, and the wait after a held replay are never in it | A role can also report its own concern in its heartbeat: the uploader is degraded with `youtube.quota_blocked` (recordings wait and the upload quota holds uploads) or `youtube.not_publishing` (uploads past their publish time and nothing confirmed public for 24 hours), see `docs/youtube-uploader.md`.
| stale | `service.stale` | Alive, but the heartbeat is older than `StaleAfterIntervals` (3) intervals, or missing |
| stopped | `service.stopped` | Not in `services.json`, or exited after a stop request |
| failed | `service.failed` | In `services.json`, gone, and no stop request was recorded |

`--output json` prints `schemaVersion` (1), `ok`, `code` (the worst role: failed, stale, degraded, ready, stopped; `service.restart_budget_exhausted` wins over all of them), `message`, `environment`, `checkedAt`, `stopRequested`, `roles[]` (state, code, cause, `causeCode`, remediation, pid, path, version, readiness, heartbeat and work ages with their limits, `lastError`, `sessionsWithoutProgress`, `lastOutcome`, `sessionOutcomes`, `logPath`, `restarts`), a `spectator` summary of `status.json`, `supervisor`, and `machine`. It exits 1 when any role is failed, stale, or degraded.

The `Machine:` line (`machine` in JSON, #251) is physical memory in use, the commit charge against the commit limit (`GetPerformanceInfo`), and the counts of `Agent.exe`, `conhost.exe`, and `HeroesOfTheStorm_x64.exe`. Each value above its `MachineHealth` limit prints a `WARN` line (`machine.warnings`, `machine.ok` false): `MemoryWarnPercent` 90, `CommitWarnPercent` 85, `AgentWarnCount` 5, `ConhostWarnCount` 40, `HeroesWarnCount` 2. A machine warning does not change a role's state, `ok`, or the exit code.

## Role logs

Each role that `services start` or the supervisor launched writes its own log file, `%LOCALAPPDATA%\HeroesReplay\logs\<role>-<yyyy-MM-dd>.log` (`ServiceRoleLogProvider`, added by `BuildHeroesReplayProvider` when `HEROESREPLAY_SERVICE_ROLE` names the role). It does not depend on the Aspire dashboard; OTLP export to Aspire stays as a second surface. The supervisor writes `supervisor-<date>.log` the same way. A role run by hand does not write one.

- One line per entry: local time with its offset, level (`INF`, `WRN`, `ERR`, `CRT`), category, event id when there is one, and the message. Exception lines follow, indented four spaces, so every line at column 0 starts an entry.
- Each process that opens the file writes a header, `--- <role> pid <n> version <v> ---`. A restarted role appends to the day's file under a new header.
- Tokens are redacted with the rules that #149 applies to `lastError` (`ServiceLogRedaction`): `access_token=`, `api_token=`, `api_key=`, `token=`, `key=`, `secret=`, `password=`, `Bearer …`, and `oauth:…`. An entry is capped at 32 KB.
- A new file starts at local midnight, and when the day's file reaches `ServiceLogs:MaxFileSizeMegabytes` (20): `<role>-<date>.1.log`, `.2.log`. Each time a file opens, the role deletes its own files older than `RetainedDays` (14, today included) and keeps at most `MaxFilesPerRole` (50). Pid files and other logs in the folder are not touched. `ServiceLogs:Directory` moves the folder; `Enabled: false` turns the files off.
- The level is `Logging:RoleFile` (Information, with `System` and `Microsoft` at Warning).
- The file is opened for shared reading: `Get-Content -Tail 50 -Wait` works while the role writes.

`services status` shows each role's newest file (or today's, before the first entry) as `Log:` and `logPath`.

## Supervision

`services start --supervise` starts the stack and then keeps that console as the supervisor. `services supervise` attaches a supervisor to a stack that is already recorded in `services.json`. `services start --roles download,youtube` starts a subset (for proofs). The supervisor is opt-in and runs in the foreground until `services stop` or Ctrl+C. Ctrl+C ends supervision only; the roles keep running.

- **Single instance.** It holds the named mutex `Local\HeroesReplay.ServiceSupervisor` for its lifetime. A second `services supervise` or `services start --supervise` exits 1, and so does a plain `services start` while it runs.
- **Each pass** (`ServiceRestart:PollInterval`, 1 s) classifies the recorded roles with the `services status` rules above (`ServiceHealthClassifier`), then applies `ServiceRestartPolicy`:
  - **failed**: restart after the backoff for the restarts already inside the budget window, `ServiceRestart:Backoff` = 10 s, 30 s, 2 min, 5 min (the last repeats). The restart is the `services start` launch (`ServiceSupervisor.Restart`): the role's prerequisites, its arguments, a new nonce, the ready file and first heartbeat. The new record replaces the old one in `services.json` as soon as it has a pid.
  - **stale**: a live role whose heartbeat is `StaleRestartAfter` (2 min) old is killed, then restarts like a failed role, against the same budget.
  - **degraded** with `causeCode` `spectate.launch_stalled`: killed with a WRN in the supervisor log, then restarted like a failed role (`lastReason` `launch_stalled`) against the same budget. With no budget left it is left up and degraded rather than killed for good.
  - Any other **degraded**, **ready**, and **stopped** role (including a role whose own console was closed with Ctrl+C) is left alone.
- **Budget.** Every restart attempt, including one that did not get ready, counts against `Budget` (5) per `BudgetWindow` (30 min). A role that goes down with the budget used stays down, the supervisor logs one error, and `services status` reports it as failed with code `service.restart_budget_exhausted` until the stack is stopped and started again.
- **Stop.** The supervisor checks `services.stop` before every pass and before every restart, and a restart's ready wait ends on it. `services stop` writes the stop file, waits up to 30 s for the supervisor to exit (then kills it), and re-reads `services.json`, so a role the supervisor was restarting is stopped with the rest. If the supervisor cannot be stopped, the stop exits 1 and leaves the stop file down so nothing restarts.
- **Spectate.** Before spectate restarts, the supervisor closes a Heroes of the Storm the dead spectator left open. It never opens OBS or the game itself. When spectate uses its restart budget and stays down, nothing drives OBS any more, so the supervisor makes a live stream this install started safe (`ObsFailSafe`, `ServiceRestart:SpectateDownObs`): `WaitingScene` (default) shows `OBS:WaitingSceneName` and the stream stays live, `StopStream` stops it, `None` leaves it. It only acts when `OBS:StreamingEnabled`, the machine is armed, OBS runs, and the stream is live.
- **Logon task.** `services install-task` registers `HeroesReplay-live` for the current user: `services start --supervise` 30 s after logon, interactive, least privilege, no time limit. Registering it needs no administrator rights. `apply-release.ps1` restarts the stack through it, and attaches `services supervise` to a stack a task or `start-live.cmd` started without one.
- **Machine.** On its first pass and then every `MachineHealth:LogInterval` (1 h), the supervisor logs the `services status` machine line, one line per `MachineHealth:WatchedProcesses` process with its private bytes (default `heroesreplay`, `obs64`, `aspire-managed`), and a warning for each value above its limit.
- **State.** `%LOCALAPPDATA%\HeroesReplay\supervisor.json` holds its pid, rules, and each role's restarts. It is written when a role changes and at least every heartbeat interval, and removed when the supervisor exits. `services status` reads it: `supervisor` (`running`, `pid`, `supervised`, `backoffSeconds`, `budget`, `budgetWindowSeconds`, `staleRestartAfterSeconds`, `logPath`) and, per role, `restarts` (`count`, `lastRestartAt`, `lastReason`, `lastFailure`, `nextRestartAt`, `budgetUsed`, `budgetLimit`, `budgetWindowSeconds`, `budgetExhausted`, `exhaustedAt`). A failed role the supervisor will restart says when in its cause.

Do not start Twitch ingest as part of this split.
