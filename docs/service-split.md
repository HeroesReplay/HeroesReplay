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
- `Data\requests.json` (and the failed file) — Twitch enqueues; the downloader fulfills; the spectator only sees local `.StormReplay` files. Both processes lock the files with a named mutex. A request leaves `requests.json` only once its replay is in `Data\Requests` (#351). A failed download keeps it queued with `Download.Attempts` and `NextAttemptAt` (backoff, no refund); one Heroes Profile answers 404 or 410, or a replay below the supported patch line, moves it to the failed file with its reason and writes a `Cancel` disposition.
- `%LOCALAPPDATA%\HeroesReplay\panel-requests.json` — `!talents` and `!stats` from `twitch connect`. The spectator consumes the pending panel and sends the hotkey.
- `Data\Standard` and `Data\Requests` — replay cache. `spectate heroesprofile` plays that cache (same shape as `spectate file`).
- `Data\Requests\<replay>.request.json` — the redemption of a requested replay. The downloader writes it before the replay file appears. The spectator links the session to it by replay id, whichever folder or path loaded the file (#165).
- `Data\redemption-dispositions.txt` — the spectator appends a `Fulfill` line when a requested match is verified, and the downloader a `Cancel` line when a request's replay can never be downloaded (#351). `twitch connect` marks the redemption FULFILLED, or CANCELED through `IRedemptionCanceller` (the points go back), and records it in `Data\redemption-fulfilled.txt`. It never cancels a redemption that also has a `Fulfill` line, and sends nothing with requests off or `Twitch:DryRunMode`. A session that was not verified writes nothing: the redemption stays UNFULFILLED and the replay plays again (#169).
- `Data\Contexts\<id>\` — recording, end screenshot, YouTube entry. The uploader already keys off these files.

## Order of work

1. This document only.
2. Done: `Engine` no longer starts `TwitchBot`. `twitch connect` uses `AddTwitchServices` and does not build the game/OCR graph.
3. Done: `heroesprofile download` lists and downloads. `spectate heroesprofile` uses `ReplayCacheProvider` and only plays files already in `Data\Standard` and `Data\Requests`. Existing files are seeded into `Data\spectated-ids.txt` so the cache is not replayed from the beginning.
4. Done: Blue/Red predictions run in `twitch connect`. The spectator does not call Helix. `twitch connect` opens a prediction when `status.json` phase is `TimerDetected`, and settles it from `completedReplayId` / `completedAt` / `completedWinnerTeam` (0 blue, 1 red, null cancels). Those completion fields are written when the spectate session ends and are not cleared when the next replay loads.
5. Done: `heroesreplay services start` launches four processes (`spectate heroesprofile`, `twitch connect`, `heroesprofile download`, `youtube uploader`) and records their pids in `%LOCALAPPDATA%\HeroesReplay\services.json`. Each process is detached in its own console window. Its pid file and, since #153, its rolling log file go to `%LOCALAPPDATA%\HeroesReplay\logs\` (see [Role logs](#role-logs)). `services stop` writes `%LOCALAPPDATA%\HeroesReplay\services.stop`. Spectate, `twitch connect`, `heroesprofile download`, and `youtube uploader` cancel on that file. The spectator then runs its normal shutdown, which stops the recording it started and closes Heroes of the Storm. Processes still alive after 20 seconds are killed, and Heroes of the Storm is closed if spectate was one of the recorded processes. Once every role has exited, stop reads OBS `GetStreamStatus` once without changing it, then stops a recording spectate claimed in `obs-recording.json` and left running, when the claiming spectate is dead (pid and start time) and the recording's duration matches the claim (`StopRecord` only, never the stream; #318). A spectate the supervisor restarts makes the same check before its first replay (#342; see Supervision). It exits 1 when a role is still running (that role stays in `services.json`), the game is still open, OBS is still streaming, or a recording spectate started may still be running. Start does not turn on Twitch ingest, and it clears a leftover stop file before launching.
6. Done: `!talents` / `!stats` and `Data\requests.json` are shared files with a cross-process lock. Chat in `twitch connect` can show a panel in the spectator process.
7. Optional later: Windows services or containers for Twitch, the downloader, and YouTube. Not for the spectator.
8. Done (#149): continuous role health. See below.
9. Done (#153): durable per-role logs and an opt-in supervisor with a bounded restart policy. See below. Windows service installation is a later slice of #130.
10. Done (#306): `services ensure`, which starts only the missing roles and never stops a running one. See [Ensure](#ensure).

## Role health

Each role writes its ready file, `%LOCALAPPDATA%\HeroesReplay\ready\<nonce>.json`, when it is ready, then rewrites it every `ServiceHealth:HeartbeatInterval` (15 s) from a timer (`ServiceHeartbeat`). The file carries `role`, `version`, `executablePath`, `nonce`, `pid`, `readiness` (`ready`, `stopping` once the stop file or Ctrl+C reaches the role, `exited` when it left its loop without one), `readyAt`, `heartbeatAt`, `heartbeatIntervalSeconds`, `lastSuccessfulWorkAt`, `lastError` (`message`, `at`), and `dependency` (the last dependency probe, see below). Spectate also writes `sessionsWithoutProgress`, `lastOutcome`, `launchingSince`, and `sessionOutcomes` (sessions this process ended, by outcome; the release health gate reads it). Error and critical logs become `lastError`, with tokens redacted.

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

### Dependency probes

Startup checks are static (files, settings, a writable cache). Since #305 each role also proves its live dependency, and reports it in its heartbeat as `dependency` (`name`, `state`, `code`, `cause`, `remediation`, `checkedAt`, `since`):

| Role | Probe | Failure codes |
| --- | --- | --- |
| download | One Heroes Profile v1 replay-list call (the call that finds the newest replay id). 401 or 403 is a rejected key; no answer, a timeout, 429, or 5xx is unreachable | `download.heroesprofile_rejected`, `download.heroesprofile_unreachable` |
| youtube | A refresh of the stored upload token at Google's token endpoint (`oauth2.googleapis.com/token`). It is not a YouTube Data API call, so it spends no quota unit and no upload call; the new access token is dropped and the token store is not written. Not used while `YouTube:DryRun` is on or YouTube is off | `youtube.oauth_invalid` (`invalid_grant`: revoked, expired, or a changed client), `youtube.oauth_missing` (no stored consent, or no `client_secrets.json`), `youtube.oauth_unreachable` |
| spectate | While OBS runs: one short read-only session (the MCP tools' client), identify, `GetVersion`, disconnect. It never touches the per-replay connection and never starts OBS; a closed OBS is `skipped`. Off until the AGENTS.md short live proof (`ServiceHealth:SpectateObsProbe` false) | `spectate.obs_rejected` (password), `spectate.obs_unreachable` |
| twitch | Twitch's token validator (`id.twitch.tv/oauth2/validate`), the read `services start` already makes for the scopes. Not used while chat, redemptions, and predictions are off (dev) | `twitch.token_invalid`, `twitch.unreachable` |

- **When.** Once before the role writes its ready file, so the first heartbeat already says whether it starts degraded, then every `ServiceHealth:DependencyProbeInterval` (10 min) while it passes and every `DependencyRetryInterval` (2 min) while it fails. Never faster than a minute. Each probe is bounded by `DependencyProbeTimeout` (10 s, at most 60 s); a probe that times out or throws is unreachable, never an exception for the role. `DependencyProbes: false` turns them off. Only a role `services start` or the supervisor launched probes.
- **Degraded, never failed.** A `rejected` or `unreachable` probe makes the live role degraded with the probe's code as `causeCode` and its fix as `remediation`. The supervisor leaves degraded roles alone, so an outage or a bad key is never a restart loop. A passing probe clears it. `ok`, `skipped` (OBS closed between replays), and `unused` (dry run, OBS off) do not change the state. The probe only reports: spectate's outage handling (Connectivity watchdog, one OBS session per replay) and the downloader's outage pause are unchanged. One exception (#358): while the download probe is `rejected`, the downloader does not call the replay list at all, and a passing probe resumes it at once (`docs/heroesprofile-api.md`).
- **Order.** `spectate.launch_stalled` still wins, so the one degraded state the supervisor restarts is unchanged. A failed probe comes before `spectate.no_match_progress`, a role's own concern (`youtube.quota_blocked`), and late work, because it names the cause.
- **No secrets.** Keys and tokens travel only in request headers or bodies. Causes are built from status codes and error names, and are redacted like `lastError`.
- **Release health gate.** Unchanged. The gate (`update release-health`) and `apply-release.ps1` count a live, heartbeating role as up whether it is ready or degraded, so a failed probe does not change a verdict. A rejected Heroes Profile key reaches the verdict only through spectate, as before: spectate still plays the cache, and nothing left to play is inconclusive.

`--output json` prints `schemaVersion` (1), `ok`, `code` (the worst role: failed, stale, degraded, ready, stopped; `service.restart_budget_exhausted` wins over all of them), `message`, `environment`, `checkedAt`, `stopRequested`, `roles[]` (state, code, cause, `causeCode`, remediation, `dependency`, pid, path, version, readiness, heartbeat and work ages with their limits, `lastError`, `sessionsWithoutProgress`, `lastOutcome`, `sessionOutcomes`, `logPath`, `restarts`), a `spectator` summary of `status.json`, `supervisor`, and `machine`. It exits 1 when any role is failed, stale, or degraded.

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

- **Single instance.** It holds the named mutex `Local\HeroesReplay.ServiceSupervisor` for its lifetime. A `Local\` mutex exists once per logon session, so another session (an SSH logon such as `ssh streampc` or `stream-pc-claude`) cannot see the desktop's mutex. There `supervisor.json` decides whether the supervisor runs (see **Running, seen from another session**, #283). `services supervise`, `services start --supervise`, and a plain `services start` make that same check before they take the mutex or start anything (`ServiceSupervisorGate`, #293). While a supervisor runs in this session or another, each exits 1 with `supervisor already running: pid 14420, seen via supervisor.json` (or `seen via its mutex` in the same session) and leaves `supervisor.json` as it was. When none runs, `services start` removes the dead supervisor's `supervisor.json`, and the new supervisor writes its own.
- **Each pass** (`ServiceRestart:PollInterval`, 1 s) classifies the recorded roles with the `services status` rules above (`ServiceHealthClassifier`), then applies `ServiceRestartPolicy`:
  - **failed**: restart after the backoff for the restarts already inside the budget window, `ServiceRestart:Backoff` = 10 s, 30 s, 2 min, 5 min (the last repeats). The restart is the `services start` launch (`ServiceSupervisor.Restart`): the role's prerequisites, its arguments, a new nonce, the ready file and first heartbeat. The new record replaces the old one in `services.json` as soon as it has a pid.
  - **stale**: a live role whose heartbeat is `StaleRestartAfter` (2 min) old is killed, then restarts like a failed role, against the same budget.
  - **degraded** with `causeCode` `spectate.launch_stalled`: killed with a WRN in the supervisor log, then restarted like a failed role (`lastReason` `launch_stalled`) against the same budget. With no budget left it is left up and degraded rather than killed for good.
  - Any other **degraded** (including a failed dependency probe, see [Dependency probes](#dependency-probes)), **ready**, and **stopped** role (including a role whose own console was closed with Ctrl+C) is left alone.
- **Budget.** Every restart attempt, including one that did not get ready, counts against `Budget` (5) per `BudgetWindow` (30 min). A role that goes down with the budget used stays down, the supervisor logs one error, and `services status` reports it as failed with code `service.restart_budget_exhausted` until the stack is stopped and started again.
- **Stop.** The supervisor checks `services.stop` before every pass and before every restart, and a restart's ready wait ends on it. `services stop` writes the stop file, waits up to 30 s for the supervisor to exit (then kills it), and re-reads `services.json`, so a role the supervisor was restarting is stopped with the rest. If the supervisor cannot be stopped, the stop exits 1 and leaves the stop file down so nothing restarts.
- **Spectate.** Before spectate restarts, the supervisor closes a Heroes of the Storm the dead spectator left open. It never opens OBS or the game itself. A restart does not run `services stop`, so the restarted spectate, before its first replay, stops the OBS recording the dead one claimed in `obs-recording.json` and left running (#342): only when the claiming pid is dead or now belongs to a process with another start time (`ProcessTable`), and OBS has recorded for about as long as the claim says. It sends `StopRecord`, never `StopStream`, logs a warning in the spectate role log, and deletes the claim. A live claimant, a recording younger than the claim, or an OBS whose websocket doesn't answer is left alone with one log line. Details: `docs/obs-operations.md` (Running and stopping). When spectate uses its restart budget and stays down, nothing drives OBS any more, so the supervisor makes a live stream this install started safe (`ObsFailSafe`, `ServiceRestart:SpectateDownObs`): `WaitingScene` (default) shows `OBS:WaitingSceneName` and the stream stays live, `StopStream` stops it, `None` leaves it. It only acts when `OBS:StreamingEnabled`, the machine is armed, OBS runs, and the stream is live.
- **Logon task.** `services install-task` registers `HeroesReplay-live` for the current user: `services start --supervise` 30 s after logon, interactive, least privilege, no time limit. Registering it needs no administrator rights. `apply-release.ps1` restarts the stack through it, and attaches `services supervise` to a stack a task or `start-live.cmd` started without one.
- **Machine.** On its first pass and then every `MachineHealth:LogInterval` (1 h), the supervisor logs the `services status` machine line, one line per `MachineHealth:WatchedProcesses` process with its private bytes (default `heroesreplay`, `obs64`, `aspire-managed`), and a warning for each value above its limit.
- **State.** `%LOCALAPPDATA%\HeroesReplay\supervisor.json` holds its pid, its process start time (`processStartedAt`, from `ProcessTable`), its rules, and each role's restarts. It is written when a role changes, at least every heartbeat interval, and just before each restart (whose ready wait can take 45 s). It is removed when the supervisor exits.
- **Running, seen from another session.** When the mutex is visible, it decides. When it is not, `ServiceSupervisorFile.IsRunning` falls back to `supervisor.json`. The supervisor counts as running only when all three hold:
  - the file's pid is alive;
  - that process started when `processStartedAt` says (within 2 s), so a reused pid does not count;
  - `updatedAt` is within 4 heartbeat intervals (60 s).

  A file from a build before #283 has no `processStartedAt`. It counts when the live pid started no later than the file's `startedAt`, because a reused pid starts only after the supervisor exited. `services status` says how it decided, for example `Supervisor: running (pid 14420, seen via supervisor.json; mutex not visible from this session)`, `running (pid 14420, seen via its mutex)`, or `not running (pid 14420 left supervisor.json; no process has pid 14420)`. Other reasons are a stale `updatedAt` and a reused pid. `services stop`, `services start` (with or without `--supervise`), `services supervise`, and the release hand-off use the same check, so `services stop` over SSH waits for the supervisor to exit as it does at the desktop, and a start over SSH does not become a second supervisor (#293).
- **Status fields.** `services status` reads the file: `supervisor` (`running`, `seenVia` (`mutex` or `supervisor.json`), `detail`, `pid`, `supervised`, `backoffSeconds`, `budget`, `budgetWindowSeconds`, `staleRestartAfterSeconds`, `logPath`) and, per role, `restarts` (`count`, `lastRestartAt`, `lastReason`, `lastFailure`, `nextRestartAt`, `budgetUsed`, `budgetLimit`, `budgetWindowSeconds`, `budgetExhausted`, `exhaustedAt`). A failed role the supervisor will restart says when in its cause.

## Ensure

`services start` is strict: it exits 1 when any role runs. `services ensure [--roles r1,r2] [--supervise] [--output text|json]` (#306) is the idempotent form for agents and scripts: make sure the requested roles (default all four) run from this install. It reads the stack with the `services status` rules (`ServiceHealthClassifier`, plus `supervisor.json`) and decides in `ServiceEnsurePlan`; it never stops or kills a role that was running.

| Code | Exit | When |
| --- | --- | --- |
| `service.ensure_noop` | 0 | Every requested role is up (ready or degraded) from this install, and a supervisor runs when `--supervise` asked for one. Nothing is started. A degraded role is left alone; its `causeCode` shows in the report. |
| `service.ensure_started` | 0 | The requested roles that are down (failed, exited after Ctrl+C, or not in `services.json`) were started in plan order through the `services start` launch (`ServiceSupervisor.Restart`: prerequisites, arguments, a new nonce, the ready file and first heartbeat). Each new record replaces the role's old one in `services.json` as soon as it has a pid; the other records stay. With `--supervise` and no supervisor anywhere, the mutex is claimed before anything starts and this console becomes the supervisor once the report is printed. |
| `service.ensure_mismatch` | 1 | A live role (requested or not) runs from another install path or another version. Builds are not mixed. |
| `service.ensure_stop_pending` | 1 | `services.stop` is down, or a stop arrived while a role was starting (that role stays in `services.json`, so `services stop` stops it). |
| `service.ensure_budget_exhausted` | 1 | A requested role used its supervisor restart budget. It stays down until `services stop` and `services start --supervise`. |
| `service.ensure_stale` | 1 | A requested role is alive but its heartbeat is stale. Ensure does not stop it; a supervisor kills and restarts it after `StaleRestartAfter`. |
| `service.ensure_supervisor_running` | 1 | A requested role is down while a supervisor runs in this session or another (`ServiceSupervisorGate` check, #293). The supervisor owns restarts, so ensure does not race it: the report says whether the supervisor restarts that role and when, or that it does not supervise it. |
| `service.ensure_start_failed` | 1 | A start failed. The roles this ensure started are killed again and `services.json` gets their earlier records back, so a failed ensure leaves the stack as it found it. Roles that were running are not touched. |
| `service.ensure_busy` | 1 | Another ensure holds `%LOCALAPPDATA%\HeroesReplay\services.ensure.lock` (one at a time across sessions, so two ensures do not start the same role twice). |

The checks run in that order (stop, mismatch, budget, stale, supervisor), and a refusal starts nothing. Starting spectate patches the OBS collection first, as `services start` does; any start also starts the Aspire dashboard when it is not up. `--output json` prints the `services status` envelope: `schemaVersion` (1), `ok`, `code`, `message`, `remediation`, `environment`, `checkedAt`, `stopRequested`, `supervise`, `supervisorRunning`, `supervisorAttached`, `requested`, `started`, and `roles[]` (`role`, `state` before the ensure, `causeCode`, `action` `running`/`start`/`started`/`start_failed`/`blocked`, `pid`, `version`, `executablePath`, `detail`). The launch lines go to stderr in JSON mode, so stdout is only the report.

Do not start Twitch ingest as part of this split.
