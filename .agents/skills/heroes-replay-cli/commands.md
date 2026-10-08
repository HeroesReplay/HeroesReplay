# heroesreplay command reference

Generated from the command tree and `CommandReference.Facts` (`src/HeroesReplay.CLI/Commands/CommandReference.cs`). Do not edit by hand: change the command or its entry, then run `dotnet test heroes-replay.slnx --filter CommandReference` with `HEROESREPLAY_WRITE_COMMAND_REFERENCE=1`. `AgentDocsTests` fails when a command or a stable code has no entry, or when this file is stale.

Each command: what must be true first, what it changes, its exit codes, and the stable codes it prints (`--output json` `code`, or a finding or role `code`). The `--output text|json` contract is in `SKILL.md`.

## `spectate file`

Spectate one .StormReplay file, or each file in a directory, then exit.

- **Options:** `--file` (`-f`), `--player`
- **Before:** Not elevated. Heroes of the Storm installed; a current-patch replay needs Battle.net signed in. OBS is optional. On ASA-SERVER only to prove a change (AGENTS.md phases). On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Launches Battle.net, HeroesSwitcher and Heroes of the Storm, sends spectator keys, and writes `Data\Contexts\<id>` and `status.json`. With OBS it changes scenes and records (`OBS:RecordingEnabled`), and streams only with `OBS:StreamingEnabled` and the arm. It mutes every microphone input (global Mic/Aux and audio input capture sources, never Desktop Audio) at each replay's session start and before it starts the stream (`OBS:MuteMicrophones`, #314). Before the first replay it sends `StopRecord` for a recording an earlier spectate claimed in `obs-recording.json` and left running, when that spectate is dead (pid and start time) and the duration matches the claim (#342); never the stream. Starts the Aspire dashboard when OTLP :4317 is down.
- **Exit:** 0 after the queue has played. 1 on a parse error (`--player` not a BattleTag, a `--file` that does not exist) or when the engine stops on an unexpected error.

## `spectate heroesprofile`

Spectate StormReplay files already in Data\Standard and Data\Requests. Does not call Heroes Profile.

- **Before:** As `spectate file`, with replays already in `Data\Standard` and `Data\Requests` (`heroesprofile download`). `services start` runs it as the spectate role.
- **Changes:** As `spectate file`, over the cached replays, in a loop. Does not call Heroes Profile.
- **Exit:** Runs until stopped (Ctrl+C, or `services stop` for a role). 1 on a parse error or an unexpected error.

## `calculators report`

Generate a spectator report for a .StormReplay file.

- **Options:** `--file` (`-f`)
- **Before:** A `.StormReplay` file or folder (`--file`, default `Location:ReplaySource`).
- **Changes:** Writes `<replay>.StormReplay.csv` to `SpectateReportPath`. Does not launch the game.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `calculators coordinates`

Validate that hero coordinates still parse from a latest-client .StormReplay file.

- **Options:** `--file` (`-f`)
- **Before:** A latest-client `.StormReplay` (`--file`).
- **Changes:** Nothing. Read only.
- **Exit:** 0 when coordinates parse and the focus timeline has entries. 1 file not found, 2 no replay object, 3 parse failed, 4 coordinates not parseable, 5 empty focus timeline.

## `calculators units`

Sample up to 5 replays per map and write unit CSV reports. Parses one file at a time.

- **Options:** `--directory` (`-d`, required), `--per-map` (`-n`), `--output` (`-o`)
- **Before:** A folder of replays (`--directory`).
- **Changes:** Writes the unit CSV reports (`--output`). Reads one file at a time; does not launch the game.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `calculators compositions`

Count team-composition labels (split push, siege, dive, poke, and the rest) and unusual-draft notes across a folder of .StormReplay files, one file at a time. Prints each replay's notes and title slot, then how often each label appears per team and per game, so the YouTube:Titles:Compositions thresholds and Frequencies can be tuned. Reads the heroes-data2 catalog under Location:DataDirectory. Read-only: it does not launch the game or write next to the replays.

- **Options:** `--directory` (`-d`, required), `--output` (`-o`)
- **Before:** A folder of replays and the heroes-data2 catalog under `Location:DataDirectory`.
- **Changes:** Writes `--output` (`.md` or `.csv`) when given; otherwise prints only. Read-only for the replays.
- **Exit:** 0 when it finishes. 1 for a missing folder, an `--output` that is not `.md` or `.csv`, or a parse error.

## `check`

Validate configuration, connectivity to Heroes Profile, OBS, Twitch, and the internet, and the ffmpeg tools clips use.

- **Options:** `--output` (`-o`)
- **Before:** Settings readable from the current directory or the exe folder. Read-only, so safe on both machines.
- **Changes:** Nothing in OBS, Twitch, or Heroes Profile: Get-style reads only (Helix GetUsers, GET /replays, an OBS Identify and GetVersion, the Battle.net window capture). Logs go to stderr.
- **Exit:** 0 when every target passes (a warning passes), 1 when one fails or on a parse error.
- **Codes:** `check.ok`

## `check config`

Bind settings and report which secrets are present.

- **Options:** `--output` (`-o`)
- **Before:** Settings readable. Resolves `op://` secrets through `op`.
- **Changes:** Nothing. Prints which secrets are present, never their values.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.config.ok`, `check.config.heroesprofile_key_missing`, `check.config.error`

## `check heroesprofile`

Call Heroes Profile GET /replays with the v1 Bearer key.

- **Options:** `--output` (`-o`)
- **Before:** `HeroesProfileApi:ApiKey` set (the v1 Bearer key).
- **Changes:** Nothing. One GET /replays (counts against the Heroes Profile allowance).
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.heroesprofile.ok`, `check.heroesprofile.key_missing`, `check.heroesprofile.request_failed`, `check.heroesprofile.error`

## `check obs`

Connect to obs-websocket 5, read the server version, verify scene files, and check the active profile and scene collection.

- **Options:** `--output` (`-o`)
- **Before:** OBS running with its WebSocket server on `OBS:WebSocketEndpoint`.
- **Changes:** Nothing. Identify, GetVersion, and the profile and collection reads, then disconnect.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.obs.ok`, `check.obs.unreachable`, `check.obs.profile_mismatch`, `check.obs.collection_mismatch`, `check.obs.selection_unreadable`, `check.obs.files_invalid`, `check.obs.error`

## `check twitch`

Call Helix GetUsers (and Predictions when enabled) for the configured channel.

- **Options:** `--output` (`-o`)
- **Before:** `Twitch:AccessToken` and `Twitch:ClientId` set.
- **Changes:** Nothing. Helix GetUsers, GetPredictions when predictions are on, and the token validation.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.twitch.ok`, `check.twitch.credentials_missing`, `check.twitch.user_not_found`, `check.twitch.predictions_scope_missing`, `check.twitch.chat_scope_missing`, `check.twitch.redemptions_scope_missing`, `check.twitch.error`

## `check client`

Verify windowed 1080p and AhliObs in Heroes of the Storm Variables.txt.

- **Options:** `--output` (`-o`)
- **Before:** None.
- **Changes:** Nothing. Reads `Variables.txt` and the AhliObs interface.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.client.ok`, `check.client.preset_mismatch`, `check.client.error`

## `check connectivity`

Probe 1.1.1.1, Twitch, and Heroes Profile without starting an OBS stream.

- **Options:** `--output` (`-o`)
- **Before:** None.
- **Changes:** Nothing. Probes 1.1.1.1, Twitch, and Heroes Profile; never starts a stream.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.connectivity.ok`, `check.connectivity.offline`, `check.connectivity.error`

## `check timer`

Read the HeroesOfTheStorm_x64 match clock from memory (read-only) and report whether it is running.

- **Options:** `--output` (`-o`)
- **Before:** Heroes of the Storm running; a match playing for a pass.
- **Changes:** Nothing. Reads the client's memory five times, a second apart; samples go to stderr in JSON mode.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.timer.ok`, `check.timer.game_not_running`, `check.timer.clock_not_running`, `check.timer.error`

## `check twitch-extension`

Report TwitchExtension:Enabled, or call uploader/whoami when the extension is on (issue 49).

- **Options:** `--output` (`-o`)
- **Before:** `TwitchExtension:Enabled`; when on, `TwitchExtension:ApiKey` (the uploader key, not the v1 key).
- **Changes:** Nothing. One uploader/whoami call when the extension is on.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.twitch_extension.ok`, `check.twitch_extension.disabled`, `check.twitch_extension.key_missing`, `check.twitch_extension.key_rejected`, `check.twitch_extension.rate_limited`, `check.twitch_extension.unreachable`, `check.twitch_extension.http_error`, `check.twitch_extension.error`

## `check battlenet`

Read the Battle.net window and report the Play or Update button.

- **Options:** `--output` (`-o`)
- **Before:** Battle.net open with a visible window; Windows OCR available.
- **Changes:** Nothing. Captures the window with PrintWindow and reads it; never clicks.
- **Exit:** 0 when the check passes (a warning passes), 1 when it fails or on a parse error.
- **Codes:** `check.battlenet.ok`, `check.battlenet.update_pending`, `check.battlenet.button_hidden`, `check.battlenet.window_missing`, `check.battlenet.ocr_unavailable`, `check.battlenet.capture_failed`, `check.battlenet.button_unread`, `check.battlenet.error`

## `check ffmpeg`

Resolve ffmpeg and ffprobe (Clips:FfmpegDirectory, the deps install folder, C:\ffmpeg\bin, PATH) and report each path and -version line. Fails when one is missing, does not run, or ffmpeg cannot encode libx264; a working build that is not the pinned version is a warning.

- **Options:** `--output` (`-o`)
- **Before:** None. Reads no secret.
- **Changes:** Nothing. Runs `ffmpeg -version`, `ffmpeg -encoders`, and `ffprobe -version`.
- **Exit:** 0 when both tools work (a build that is not the pinned one is a warning and passes), 1 when one is missing, does not run, or cannot encode libx264.
- **Codes:** `check.ffmpeg.ok`, `check.ffmpeg.not_pinned`, `check.ffmpeg.missing`, `check.ffmpeg.not_runnable`, `check.ffmpeg.libx264_missing`, `check.ffmpeg.error`

## `client configure`

Write Variables.txt (windowed 1080p, AhliObs) and copy the StormInterface into Documents.

- **Before:** Heroes of the Storm closed (it rewrites `Variables.txt` on exit).
- **Changes:** Writes root and account `Variables.txt` (windowed 1080p, AhliObs) and copies the AhliObs `.StormInterface` into Documents.
- **Exit:** 0 when the interface was copied, 1 otherwise or on an unexpected error.

## `client status`

Report whether Variables.txt and AhliObs match the spectator preset.

- **Options:** `--output` (`-o`)
- **Before:** None.
- **Changes:** Nothing. Reads `Variables.txt` and the interface.
- **Exit:** 0 when the preset matches, 1 on a mismatch, when the status cannot be read, or on a parse error.
- **Codes:** `client.preset_ok`, `client.preset_mismatch`, `client.error`

## `client firewall`

Add an inbound Windows Firewall rule for each installed Heroes client exe. Needs an elevated shell; spectate itself does not.

- **Before:** An elevated shell (the only command that needs one).
- **Changes:** Adds the inbound rule `HeroesReplay inbound Base<build>` for each installed client exe that no enabled Allow rule covers; replaces duplicates of its own rule.
- **Exit:** 0 when every installed client has an Allow rule, 1 when not elevated or a rule could not be added.

## `otel up`

Start the Aspire dashboard with the Aspire CLI (UI :18888, OTLP gRPC :4317).

- **Before:** The local Aspire CLI tool (`dotnet tool restore`).
- **Changes:** Starts the Aspire dashboard (UI :18888, OTLP :4317) and records its pid.
- **Exit:** 0 when the dashboard listens, 1 otherwise.

## `otel down`

Stop the Aspire dashboard process started by heroesreplay.

- **Before:** None. It stops only a dashboard this CLI started.
- **Changes:** Stops the dashboard this CLI started (its recorded pid tree).
- **Exit:** 0 when it stopped it or nothing was running, 1 when it could not stop it.

## `otel status`

Show whether the Aspire dashboard is listening and the OTLP endpoint the CLI uses.

- **Before:** None.
- **Changes:** Nothing. Read only.
- **Exit:** 0 when OTLP :4317 listens, 1 otherwise.

## `mcp`

Run a stdio MCP server so an agent can read spectator status, run integration checks, and inspect, validate, and screenshot OBS read-only. Logs go to stderr.

- **Before:** Run by an MCP client over stdio (`.mcp.json`).
- **Changes:** Nothing: every tool is read-only, and the OBS tools send only Get requests and never return the stream key. Logs go to stderr.
- **Exit:** Runs until the client closes stdin.
- **Codes:** `obs.unreachable`, `obs.auth_failed`, `obs.password_unresolved`, `obs.settings_unreadable`, `obs.request_failed`, `obs.source_not_found`

## `twitch connect`

Connect chat, sync channel-point rewards, and watch spectator status for Blue/Red predictions. Does not launch the game.

- **Before:** Twitch token with the chat and redemption scopes. Dev turns every side effect off (`DryRunMode`). On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Joins chat, syncs channel-point rewards, queues redemptions in `Data\requests.json`, and opens and resolves Blue/Red predictions.
- **Exit:** Runs until stopped (Ctrl+C, or `services stop` for a role). 1 on a parse error or an unexpected error.

## `twitch say`

Connect chat and send one message to the configured channel.

- **Options:** `--message` (required)
- **Before:** `--message`. Sends to the live channel's chat when the settings name it.
- **Changes:** Sends one chat message.
- **Exit:** 0 when sent, 1 without `--message` or when chat did not join the channel.

## `twitch rewards generate`

serializes the default rewards with default properties.

- **Before:** None.
- **Changes:** Writes the default catalog to `Data\custom-rewards.json`. Does not call Twitch.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `twitch rewards submit`

Creates or updates channel-point rewards from the catalog and deletes leftover Unranked Draft rewards.

- **Before:** A broadcaster token with `channel:manage:redemptions`.
- **Changes:** Creates or updates the channel-point rewards on the channel and deletes leftover Unranked Draft rewards.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `twitch rewards list`

List custom channel-point rewards on the Twitch channel.

- **Before:** A broadcaster token.
- **Changes:** Nothing. Read only.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `twitch rewards remove-unranked-draft`

Delete channel-point rewards titled Unranked Draft or (UD). Leaves Quick Match, Storm League, and ARAM rewards.

- **Before:** A broadcaster token with `channel:manage:redemptions`.
- **Changes:** Deletes `(UD)` / Unranked Draft rewards only.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `twitch rewards test`

Run the local reward handler as if a viewer redeemed a channel-point reward.

- **Options:** `--title`, `--message`
- **Before:** None.
- **Changes:** Runs the local reward handler as if a viewer redeemed (`--title`, `--message`): it can queue a request in `Data\requests.json`.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `twitch predictions test`

Create a 30s Blue/Red prediction on the channel, then resolve or cancel it.

- **Options:** `--outcome`
- **Before:** A token with `channel:manage:predictions`. Creates a real prediction on the configured channel.
- **Changes:** Creates a 30 s Blue/Red prediction, then resolves (`--outcome Blue|Red`) or cancels it (default).
- **Exit:** 0 when it finishes. 1 on any other `--outcome` (a parse error) or an unexpected error.

## `youtube uploader`

Upload OBS recordings of replays

- **Before:** `Data\client_secrets.json` and consent for real uploads; dev and base settings are a dry run. On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Uploads recordings from `Data\Contexts` (private, scheduled by the publication budget), deletes each mp4 after its insert, and runs the library pass. Dry run writes `youtube-dry-run.json` instead.
- **Exit:** Runs until stopped (Ctrl+C, or `services stop` for a role). 1 on a parse error or an unexpected error.

## `youtube library`

Run the YouTube library pass now: list the channel's uploads, record videos missing from Data\youtube-library.jsonl (Heroes Profile fills a missing map, mode, rank, or build), and file them into the playlist groups YouTube:Playlists turns on (map, mode, Storm League rank, unusual draft, viewer review, patch). The uploader that services start launches runs the same pass at most every YouTube:LibraryInterval. Both share the daily quota units in Data\youtube-quota-units.json, and only one process runs the pass at a time. Without --once this repeats the pass each LibraryInterval until stopped. Dry-run writes Data\youtube-library-dry-run.json and does not call YouTube.

- **Options:** `--once`
- **Before:** The `{ChannelId}-library` consent; run once at the machine to grant it.
- **Changes:** Records missing videos in `Data\youtube-library.jsonl` and inserts playlist items (50 units each). Dry run writes `Data\youtube-library-dry-run.json` and calls nothing.
- **Exit:** With `--once`: 0 when the pass finished, 1 when it failed. Without it, runs until stopped.

## `heroesprofile download`

List and download Storm League replays into Data\Standard. Does not launch the game.

- **Before:** `HeroesProfileApi:ApiKey`.
- **Changes:** Downloads Storm League replays into `Data\Standard` and requested ones into `Data\Requests`; keeps the hero statistics current while `YouTube:Titles:StatHooks:Enabled`. A Standard replay whose download Heroes Profile refuses with an HTTP status is skipped (replay id and status logged), not counted as an outage. Does not launch the game.
- **Exit:** Runs until stopped (Ctrl+C, or `services stop` for a role). 1 on a parse error or an unexpected error.

## `heroesprofile hero-stats`

Fetch the Heroes Profile hero statistics for YouTube title hooks now, for the newest patch and each HeroesProfileApi:HeroStats:GameTypes entry, into Data\HeroesProfile\hero-stats.

- **Before:** `HeroesProfileApi:ApiKey` with a plan that allows the statistics calls. About 92 calls and 15 to 20 minutes.
- **Changes:** Writes `Data\HeroesProfile\hero-stats\<patch>-<code>.json` and deletes files older than the newest two patches.
- **Exit:** 0 when a file was written, 1 on a 401 or 403, a 422, or when nothing was written.

## `heroesprofile patch-index`

Find the first Heroes Profile replay id on the same patch line as the latest replay. Every build iteration of that line counts.

- **Options:** `--write`
- **Before:** `HeroesProfileApi:ApiKey`.
- **Changes:** Nothing, unless `--write`: then it stores `MinReplayId` in `appsettings.json`.
- **Exit:** 0 when it finishes, 1 when the latest replay's version or (with `--write`) `appsettings.json` or its `MinReplayId` cannot be found.

## `heroesprofile sample`

Download the newest Heroes Profile replays of one map into a folder for calculators units and calculators report. Never into the spectate queue.

- **Options:** `--map` (required), `--count`, `--game-type`, `--output` (required)
- **Before:** `HeroesProfileApi:ApiKey`; `--output` must not be a spectate queue folder.
- **Changes:** Downloads the newest listed replays of `--map` into `--output` until `--count` are there or the listing runs out. A download that still fails after the Heroes Profile retries (a 429 waits for `Retry-After`) is skipped: its replay id and HTTP status are logged, its partial file is deleted, and the summary lists each skip with its reason.
- **Exit:** 0 when at least one listed replay is in `--output` (downloaded or already there), even with skips. 1 when `--count` is out of 1 to 20, `--output` is a queue folder, nothing was listed, or every listed replay was skipped.

## `services start`

Start spectate, twitch connect, heroesprofile download, and youtube uploader. Does not start Twitch ingest.

- **Options:** `--supervise`, `--roles`
- **Before:** No supervisor running in any session (with `--supervise`). On ASA-SERVER prove supervision with `--roles download,youtube` only. On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Starts the roles as separate processes (spectate, twitch, download, youtube), updates the OBS collection paths while OBS is closed, starts the Aspire dashboard when needed, and with `--supervise` keeps this console as the supervisor. Never starts Twitch ingest.
- **Exit:** 0 when every role is ready (with `--supervise`: when supervision ends). 1 on an unknown role, a running supervisor, or a role that did not start.

## `services ensure`

Make sure the requested roles run from this install: start only the ones that are down (failed, exited, or never started), through the same startup checks as `services start`, and leave running ones alone. Never stops a running role and never mixes builds. With --supervise, attaches a supervisor when none runs. Exit 0: service.ensure_noop (nothing to do) or service.ensure_started. Exit 1, starting nothing: service.ensure_mismatch (a role runs from another install path or version), service.ensure_stop_pending (services.stop is down), service.ensure_budget_exhausted, service.ensure_stale (a requested role is alive but stale), service.ensure_supervisor_running (a requested role is down while a supervisor runs in any session; the supervisor owns its restarts), service.ensure_busy (another ensure runs). service.ensure_start_failed stops again what this ensure started.

- **Options:** `--supervise`, `--roles`, `--output` (`-o`)
- **Before:** This install's build. Starts nothing while another build, a stop, a stale role, or a supervisor that owns the restart is in the way.
- **Changes:** Starts only the requested roles that are down, through the same checks as `services start`; never stops a running role. With `--supervise`, becomes the supervisor when none runs.
- **Exit:** 0 for `service.ensure_noop` or `service.ensure_started`; 1 for every other code or a parse error.
- **Codes:** `service.ensure_noop`, `service.ensure_started`, `service.ensure_mismatch`, `service.ensure_stop_pending`, `service.ensure_budget_exhausted`, `service.ensure_supervisor_running`, `service.ensure_stale`, `service.ensure_start_failed`, `service.ensure_busy`

## `services stop`

Ask the recorded processes to shut down, kill any still running after 20 seconds, close Heroes of the Storm, and stop an OBS recording spectate left running (never the stream). Exits 1 unless every role exited, the game closed, OBS is not streaming, and no recording spectate started is still running.

- **Before:** On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Writes `services.stop`, stops the supervisor, then the roles (kills any still running after 20 s), and closes Heroes of the Storm. Once every role has exited, sends `StopRecord` for a recording spectate claimed in `obs-recording.json` and left running, when the claiming spectate is dead (pid and start time) and the duration matches the claim (#318, #342). Never stops an OBS stream.
- **Exit:** 0 when every role and the supervisor exited, the game closed, OBS is closed or not streaming, and no recording spectate started is left running. 1 otherwise, including a running OBS whose websocket does not answer on an install that streams, and a claimed recording that OBS refused to stop or that could not be checked.

## `services status`

Classify each role as ready, degraded, stale, stopped, or failed from its heartbeat, with the cause and the fix, plus the spectator status file. Exits 1 when a role is failed, stale, or degraded.

- **Options:** `--output` (`-o`)
- **Before:** None. Over SSH the supervisor's mutex is not visible, so `supervisor.json` decides.
- **Changes:** Nothing. Read only.
- **Exit:** 0 when no role is failed, stale, or degraded; 1 otherwise.
- **Codes:** `service.ready`, `service.degraded`, `service.stale`, `service.stopped`, `service.failed`, `service.restart_budget_exhausted`, `spectate.no_match_progress`, `spectate.launch_stalled`, `download.heroesprofile_rejected`, `download.heroesprofile_unreachable`, `twitch.token_invalid`, `twitch.unreachable`, `youtube.oauth_invalid`, `youtube.oauth_missing`, `youtube.oauth_unreachable`, `youtube.quota_blocked`, `youtube.not_publishing`, `spectate.obs_rejected`, `spectate.obs_unreachable`

## `services supervise`

Supervise the roles `services start` recorded, in the foreground: restart failed roles with backoff (10s, 30s, 2m, 5m), kill and restart roles whose heartbeat is 2 minutes old, at most 5 restarts per role in 30 minutes (ServiceRestart), then leave the role down (service.restart_budget_exhausted). One supervisor at a time, across logon sessions (an SSH session sees the desktop's through supervisor.json). `services stop` ends it; Ctrl+C leaves the roles running unsupervised.

- **Before:** Roles recorded by `services start`; no other supervisor in any session.
- **Changes:** Restarts failed roles with backoff, kills and restarts stale ones, writes `supervisor.json` and its log, and makes a live stream safe when spectate stays down (`ServiceRestart:SpectateDownObs`).
- **Exit:** Runs until `services stop` or Ctrl+C (which leaves the roles unsupervised). 1 when a supervisor already runs.
- **Codes:** `service.restart_budget_exhausted`

## `services install-task`

Register the Windows scheduled task HeroesReplay-live: `services start --supervise` from this install when you log on, interactive and not elevated (no administrator rights needed). apply-release.ps1 restarts the stack through it after an update, so the stack comes back supervised. --remove deletes it.

- **Options:** `--environment`, `--roles`, `--name`, `--remove`
- **Before:** Run once from the release install on the stream PC. No administrator rights.
- **Changes:** Registers (or with `--remove` deletes) the scheduled task `HeroesReplay-live` that runs `services start --supervise` at logon.
- **Exit:** 0 when it finishes. 1 on a parse error or an unexpected error.

## `obs arm`

Allow this machine to start Twitch ingest. Writes %LOCALAPPDATA%\HeroesReplay\stream-armed. Run it only on the stream PC; the spectator still needs OBS:StreamingEnabled (prod).

- **Before:** Only on the stream PC, or on ASA-SERVER for a stream proof.
- **Changes:** Writes `%LOCALAPPDATA%\HeroesReplay\stream-armed`. OBS is not touched.
- **Exit:** 0 when armed, 1 when the file could not be written.

## `obs disarm`

Stop this machine from starting Twitch ingest. Deletes the arm file. A stream that is already live keeps running until services stop or OBS stops it.

- **Before:** None.
- **Changes:** Deletes the arm file. A live stream keeps running.
- **Exit:** 0 when disarmed or already not armed, 1 when the file could not be deleted.

## `obs status`

Print the stream arm, OBS:StreamingEnabled, and the expected OBS profile and scene collection. Does not connect to OBS.

- **Options:** `--output` (`-o`)
- **Before:** None. Does not connect to OBS or resolve secrets.
- **Changes:** Nothing. Read only.
- **Exit:** 0 unless the settings cannot be loaded (1).
- **Codes:** `obs.ingest_ready`, `obs.stream_not_armed`, `obs.streaming_disabled`, `obs.settings_unreadable`

## `obs pages`

Render the report-scene pages in Location:DataDirectory (queue.html, prediction-report.html) with this build, from the saved request queue and the last prediction report, then reload the OBS browser sources that show them. The reload is the only change made in OBS; it is skipped when OBS is not running. Exit 1 only when a page could not be written.

- **Options:** `--no-reload`
- **Before:** None. OBS optional.
- **Changes:** Writes `Data\queue.html` and `Data\prediction-report.html`, then reloads the browser sources that show them (`refreshnocache`) unless `--no-reload` or OBS is closed. The reload is the only OBS change.
- **Exit:** 0 when every page was written, 1 when one could not be.

## `obs inspect`

Read live OBS without changing it: versions, active profile and scene collection, canvas and FPS, output mode, recording format, encoders and bitrates, the record directory, scenes, inputs and global audio, stream and record status, stats, the stream service (never the key), and the stream arm. Exit 1 when OBS cannot be read.

- **Options:** `--output` (`-o`)
- **Before:** OBS running with its WebSocket server. Safe on the live box.
- **Changes:** Nothing. Get requests only; the stream key is never read out.
- **Exit:** 0 when OBS was read, 1 when it could not be.
- **Codes:** `obs.unreachable`, `obs.auth_failed`, `obs.password_unresolved`, `obs.settings_unreadable`, `obs.request_failed`

## `obs validate`

Check the collection OBS has loaded against obs/Default.json and this install's settings without changing it: the install's OBS files against obs/bundle.manifest, the websocket requests HeroesReplay sends, profile and collection, scenes, sources and filters, where each driven item and the game capture are placed, asset paths, Mic/Aux, canvas 1920x1080 and FPS, the recording format (.mp4, and crash-safe: fragmented MP4) and OBS:RecordingFormat, the stream and recording bitrate floor, and the stream service when OBS:StreamingEnabled. Findings have stable codes. Exit 0 when there is no error finding, 1 otherwise or when OBS cannot be read.

- **Options:** `--output` (`-o`)
- **Before:** OBS running with its WebSocket server. Safe on the live box. Over SSH a path through the `C:\heroesreplay` junction is checked at the junction's target, and one it cannot check is `obs.file_unverifiable`, a warning (skill `heroes-replay-obs`).
- **Changes:** Nothing. Get requests only.
- **Exit:** 0 when no finding is an error, 1 otherwise or when OBS cannot be read.
- **Codes:** `obs.bundle_missing`, `obs.bundle_invalid`, `obs.bundle_unverified`, `obs.asset_missing`, `obs.request_unavailable`, `obs.scene_missing`, `obs.source_missing`, `obs.source_kind_mismatch`, `obs.scene_item_missing`, `obs.scene_item_misplaced`, `obs.bitrate_low`, `obs.collection_custom`, `obs.file_missing`, `obs.file_unverifiable`, `obs.runtime_file_missing`, `obs.path_stale`, `obs.url_invalid`, `obs.mic_enabled`, `obs.mic_muted`, `obs.canvas_mismatch`, `obs.fps_low`, `obs.profile_unreadable`, `obs.recording_format`, `obs.recording_not_crash_safe`, `obs.recording_format_invalid`, `obs.stream_key_missing`, `obs.stream_service_unexpected`, `obs.filter_missing`, `obs.filter_stale`, `obs.profile_mismatch`, `obs.collection_mismatch`, `obs.selection_unreadable`, `obs.unreachable`, `obs.auth_failed`, `obs.password_unresolved`, `obs.settings_unreadable`, `obs.request_failed`

## `obs bundle`

Check this install's OBS files against obs\bundle.manifest without connecting to OBS: each file's size and SHA-256, and that obs\Default.json has the scene and source contract. Exit 1 on obs.bundle_invalid. A source checkout's plain asset list is checked for presence only; a folder with no manifest is reported and exits 0. --write is the packaging step tools/package-release.ps1 runs on its publish folder.

- **Options:** `--install`, `--write`, `--output` (`-o`)
- **Before:** An install or publish folder with `obs\Default.json` (`--install`, default this exe's folder). No OBS needed.
- **Changes:** Nothing, unless `--write` (packaging only, refused in a source checkout): then it writes `obs\bundle.manifest`.
- **Exit:** 0 when the files match (or there is no manifest), 1 on `obs.bundle_invalid`, `obs.bundle_missing`, or a failed `--write`.
- **Codes:** `obs.bundle_invalid`, `obs.bundle_missing`

## `obs plan`

Show what an update would change in the live OBS scene collection, without changing anything. Reads files only (no websocket), so it is safe while OBS runs. It compares the live collection with the install's obs/Default.json, after the path rewrite, property by property for every source, filter, and scene item, and with the template the collection was last written from: each difference is a managed change, managed addition or removal (the template moved), an operator override, addition, or removal (kept), a conflict (both changed), or unattributed (no base). It also says what `update install-obs` would do now (create, replace, update paths, restore a waiting rollback, or keep a custom collection), and whether that waits for OBS. Exit 0 when nothing conflicts, 1 on a conflict or when the collection or template cannot be read.

- **Options:** `--install`, `--previous`, `--environment`, `--output` (`-o`)
- **Before:** None. Reads files only (no websocket), so it is safe while OBS runs.
- **Changes:** Nothing. Compares the live collection with the install's `obs/Default.json` and the template it was last written from.
- **Exit:** 0 when nothing conflicts, 1 on a conflict or when the settings, the collection, or the template cannot be read.
- **Codes:** `obs.plan_in_sync`, `obs.plan_changes`, `obs.plan_conflict`, `obs.plan_base_unknown`, `obs.collection_custom`, `obs.collection_missing`, `obs.collection_unreadable`, `obs.template_missing`, `obs.settings_unreadable`

## `obs apply`

Merge the install's obs/Default.json changes into the live OBS scene collection and keep the operator's overrides, additions, and removals. Three-way, with the template the collection was last written from (the SHA-256 in managed-collections.json: this install's template, --previous's, or the copy in %LOCALAPPDATA%\HeroesReplay\obs\templates), as `obs plan` shows it. Without --backup it writes nothing and shows the merge, also while OBS runs. With --backup, OBS must be closed (refused while it runs, obs.apply_obs_running): the live collection is backed up to %LOCALAPPDATA%\HeroesReplay\obs\backups, the merge is written atomically, and managed-collections.json names this template (and that the collection keeps the operator's work, so no update replaces it); `obs restore <backup>` undoes it, record included. Refused on a conflict (obs.apply_conflict), without a known base (obs.apply_base_unknown), while a release rollback waits (obs.apply_rollback_pending), and when the merge does not compare as the template plus the operator's work (obs.apply_unverified). Exit 0 when merged, in sync, or ready; 1 when refused or the collection or template cannot be read.

- **Options:** `--backup`, `--install`, `--previous`, `--environment`, `--output` (`-o`)
- **Before:** None without `--backup` (reads files only, also while OBS runs). With `--backup`: OBS closed (refused while it runs), no release rollback waiting. On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Nothing without `--backup`. With it: makes the stable asset copy when `OBS:StableAssets` is on, backs up the live collection, writes the three-way merge (the template's changes, the operator's overrides, additions and removals kept) atomically, saves the template in `%LOCALAPPDATA%\HeroesReplay\obs\templates`, and records it in `managed-collections.json` and `apply-undo.json` (`obs restore` of that backup puts the record back).
- **Exit:** 0 when merged, in sync, or ready (without `--backup`); 1 when refused (conflict, unknown base, OBS running, a waiting rollback, an unverified merge, a worktree asset folder) or the settings, the collection, or the template cannot be read.
- **Codes:** `obs.applied`, `obs.apply_ready`, `obs.apply_in_sync`, `obs.apply_conflict`, `obs.apply_base_unknown`, `obs.apply_obs_running`, `obs.apply_rollback_pending`, `obs.apply_unverified`, `obs.apply_failed`, `obs.collection_missing`, `obs.collection_unreadable`, `obs.template_missing`, `obs.settings_unreadable`

## `obs backup`

Copy the live OBS scene collection (OBS:SceneCollectionName) into %LOCALAPPDATA%\HeroesReplay\obs\backups, the folder every HeroesReplay write backs it up to (the newest 10 are kept), and list its backups with their time, size and SHA-256. Only reads the collection, so it is safe while OBS runs. --list lists without copying. Exit 1 when there is no collection or the copy failed.

- **Options:** `--list`, `--output` (`-o`)
- **Before:** None. Reads the live collection only, so it is safe while OBS runs.
- **Changes:** Copies the live collection into `%LOCALAPPDATA%\HeroesReplay\obs\backups` (the newest 10 are kept), unless `--list`.
- **Exit:** 0 when backed up or listed, 1 when the settings cannot be read, there is no collection, or the copy failed.
- **Codes:** `obs.backed_up`, `obs.backups_listed`, `obs.collection_missing`, `obs.restore_failed`, `obs.settings_unreadable`

## `obs restore`

Write a backup of the live OBS scene collection back over it, byte for byte, while OBS is closed. The collection it replaces is backed up first (so a restore can be undone with the next backup), the write is atomic, and a release rollback that waited for OBS is cleared. managed-collections.json is not changed. Refused while OBS runs (obs.restore_obs_running), and for a file that is not a backup of this collection (obs.backup_other_file) or not a scene collection (obs.backup_invalid). Exit 1 when nothing was restored.

- **Options:** `--output` (`-o`)
- **Before:** OBS closed (refused while it runs). A backup of this collection. On DESKTOP-8SJEK72 only with the owner, in a scheduled downtime.
- **Changes:** Backs up the current collection, then writes the backup over it atomically and clears a waiting release rollback. `managed-collections.json` is not changed, unless the backup is the one `obs apply` took: then its record goes back to what it was before the apply.
- **Exit:** 0 when restored or already the same, 1 when nothing was restored.
- **Codes:** `obs.restored`, `obs.already_restored`, `obs.restore_obs_running`, `obs.backup_missing`, `obs.backup_other_file`, `obs.backup_invalid`, `obs.restore_failed`, `obs.settings_unreadable`

## `update check`

Print the installed version and the latest release tag. Does not download or restart.

- **Before:** Network access to the GitHub API.
- **Changes:** Nothing. Does not download or restart.
- **Exit:** 0 when the latest release was read, 1 when it could not be or has no `heroesreplay-win-x64.zip`.

## `update preserve-min-replay-id`

Keep the higher MinReplayId when a release replaces appsettings.json.

- **Options:** `--previous` (required), `--target` (required)
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.
- **Changes:** Writes the higher `MinReplayId` into the new install's `appsettings.json`.
- **Exit:** 0 when done, 1 when a file cannot be read or written.

## `update release-health`

Called by apply-release.ps1 after it installs a release. Exit 0 once every role in services.json runs with a fresh heartbeat from a process started after --since, and spectate showed match progress (the match clock or the award screen) after it. Exit 1 while Release:HealthWindow is still open (without --wait). When it closes: 2 when a role is down or stale or every replay spectate tried failed (roll back), 4 when spectate had nothing it could play (inconclusive: keep the install). 3 when a stop was requested (no verdict).

- **Options:** `--since` (required), `--install`, `--environment`, `--window`, `--wait`
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.
- **Changes:** Nothing. Read only.
- **Exit:** 0 healthy; 1 while `Release:HealthWindow` is open (without `--wait`); 2 a role down or stale, or every replay failed (roll back); 3 a stop was requested (no verdict); 4 nothing playable (inconclusive, keep).

## `update migrate-stream-arm`

Called by apply-release.ps1 once: arm this machine for Twitch ingest when the install being replaced has OBS:StreamingEnabled true. Never arms when it is false, and never runs twice.

- **Options:** `--previous` (required), `--environment`
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.
- **Changes:** Arms this machine once when the replaced install streamed (`OBS:StreamingEnabled` true).
- **Exit:** 0 when done or not needed, 1 when the settings or the arm file could not be read or written.

## `update install-obs`

Called by apply-release.ps1: check the release's obs folder against obs\bundle.manifest (sizes and SHA-256) and refuse a mismatch with obs.bundle_invalid (exit 1, nothing written; a release with no manifest installs with a warning), then replace the scene collection HeroesReplay manages with the release's (backed up to %LOCALAPPDATA%\HeroesReplay\obs\backups, written atomically), merge the release's template changes into a custom one where the operator only added scenes, sources, filters, or settings (keeping them), keep any other custom one (a conflict is listed), and install the profile template only when this machine has no profile. Every template it writes from is kept in %LOCALAPPDATA%\HeroesReplay\obs\templates as the base of a later merge. While OBS is running nothing is written; the collection is replaced (or merged) the next time HeroesReplay finds OBS closed. Never copies service.json.

- **Options:** `--install` (required), `--previous`, `--environment`
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.
- **Changes:** Checks the release's `obs` folder against `obs\bundle.manifest`, then replaces a managed collection (with a backup), merges the template's changes into a custom one where the operator only added (keeping the additions), keeps any other custom one, saves the templates in `%LOCALAPPDATA%\HeroesReplay\obs\templates`, and installs the profile template only when the machine has none. Writes nothing while OBS runs.
- **Exit:** 0 when done or deferred, 1 on `obs.bundle_invalid` or a failed copy.
- **Codes:** `obs.bundle_invalid`

## `update restore-obs`

Called by apply-release.ps1 on a rollback, while the failed build is still installed: put back the OBS scene collection the restored install (--previous) ran with, the backup taken before the failed release first wrote it (%LOCALAPPDATA%\HeroesReplay\obs\release-rollback.json). OBS closed: the backup's bytes are written back. OBS running: it goes in through the spare collection {collection}-next without stopping the stream or a recording (OBS:LiveCollectionSwap); when that cannot run, the restore waits in restore-pending.json (services status shows it) until the restored build finds OBS closed or swaps it at its next replay. A custom collection is never overwritten, and nothing happens when the release did not write the collection. Exit 1 when the collection was kept as it is (custom, unreadable, another install's record) or OBS stayed on the spare.

- **Options:** `--previous` (required), `--install`, `--environment`
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box. Runs on a rollback.
- **Changes:** Puts back the collection the restored install ran with: written while OBS is closed, or swapped in through `{collection}-next` while it runs, or left pending in `restore-pending.json`.
- **Exit:** 0 when restored or nothing to do, 1 when the collection was kept (custom, unreadable, another install's record) or OBS stayed on the spare.

## `update launcher`

Called by apply-release.ps1: rewrite %LOCALAPPDATA%\HeroesReplay\start-live.cmd to start every role supervised (services start --supervise), keeping the replaced one as start-live.cmd.previous. With --restore, put that one back for a rollback.

- **Options:** `--install`, `--environment`, `--restore`, `--state-dir`
- **Before:** Run by `apply-release.ps1` with the new build. Do not run it by hand on a dev box.
- **Changes:** Rewrites `%LOCALAPPDATA%\HeroesReplay\start-live.cmd` to `services start --supervise` (keeping `start-live.cmd.previous`), or puts the previous one back with `--restore`.
- **Exit:** 0 when done, 1 without `--install` (and no `--restore`) or when the file could not be written.

## `deps install`

Download the pinned ffmpeg build, check its size and SHA-256, and extract only ffmpeg.exe and ffprobe.exe into <dir>\ffmpeg. Does nothing when that build is already installed. Safe to rerun: files are staged, then moved into place, so an exe is never half-written. apply-release.ps1 runs it after each install; a failure there only warns. Exit 1 when the download, the hash, or the copy fails.

- **Options:** `--dir`, `--output` (`-o`)
- **Before:** Network access to the pinned download URL. CI never runs it.
- **Changes:** Downloads, checks, and installs `ffmpeg.exe` and `ffprobe.exe` into `<dir>\ffmpeg` (staged, then moved into place); nothing when the pinned build is already there.
- **Exit:** 0 when installed or already installed, 1 when the download, the hash, or the copy failed.
- **Codes:** `deps.installed`, `deps.already_installed`, `deps.failed`

## `config effective`

Print every effective setting with the layer that set it (base appsettings.json, appsettings.secrets.json, the appsettings.{env}.json overlay, then HEROES_REPLAY_ variables; a later layer wins), the layers it overrides, the overlay order, and the environment name. Secrets are always redacted, with no switch to show them: every key appsettings.secrets.json sets and every key whose name contains Key, Token, Secret, Password, Credential, or ConnectionString shows as (set) or (empty), and an op:// value as (op:// reference), never resolved. Variables set only in start-live.cmd apply to the processes it starts, not to this shell. Exit 1 when appsettings.json is missing or unreadable, or --section matches nothing.

- **Options:** `--section`, `--environment`, `--install`, `--redact`, `--output` (`-o`)
- **Before:** None. Resolves no `op://` reference.
- **Changes:** Nothing. Secrets are always redacted.
- **Exit:** 0 when printed, 1 when `appsettings.json` is missing or unreadable, or `--section` matches nothing.
- **Codes:** `config.base_missing`, `config.unreadable`, `config.section_not_found`
