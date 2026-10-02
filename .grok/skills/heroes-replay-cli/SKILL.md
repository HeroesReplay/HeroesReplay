---
name: heroes-replay-cli
description: >
  heroesreplay CLI: spectate, services, heroesprofile, calculators, check, client, otel, twitch,
  youtube, obs, update, mcp, secrets, op://.
  Use when adding or changing commands, validating integrations, running the exe,
  or /heroes-replay-cli.
---

# heroesreplay CLI

Entry: `src/HeroesReplay.CLI`. Assembly name `heroesreplay`. System.CommandLine 2 (`Subcommands`, `SetAction`). No command needs administrator rights except `client firewall`. `spectate` runs unelevated (issue #133): launching Battle.net and HeroesSwitcher, PrintWindow capture, OCR, the memory clock, and hotkeys all work at normal integrity. Only adding Windows Firewall rules needs an elevated shell. Invalid input is a parse error and exits 1 before anything runs.

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- <command>
```

`--no-launch-profile` is required; launchSettings would otherwise inject leftover spectate args.

**ASA-SERVER** is for developing and proving the spectator, CLI, and services. The phases are in `AGENTS.md` (unit, then build, then one replay, then 1–5 full games only when the loop itself changed). Stop with `heroesreplay services stop`, which also closes Heroes of the Storm. If you kill `heroesreplay` yourself, close the game too (`CloseMainWindow`, then `Kill` if it does not exit). An open client with no spectator is a stuck replay. Do not start Twitch ingest. **DESKTOP-8SJEK72** is the production spectate and live stream. Do not kill or rebuild it.

## Commands

| Command | Behavior |
| --- | --- |
| `spectate file [--file path] [--player 1-10]` | Play one `.StormReplay` or each file in a directory, then exit. `--file` defaults to `Location:ReplaySource`. `--player` follows that hero (1-10, or 0 for the tenth) while it is alive. A `--player` outside that, or a `--file` path that does not exist, is a parse error (exit 1). Exits 1 when the engine stops on an unexpected error. Starts the Aspire dashboard when OTLP :4317 is down; dashboard failure does not fail the replay. |
| `spectate heroesprofile` | Play `.StormReplay` files already in `Data\Standard` and `Data\Requests`. Does not call Heroes Profile. Same dashboard startup as `spectate file`. |
| `heroesprofile download` | List and download Storm League replays into `Data\Standard`, and requested replays into `Data\Requests`. Does not launch the game. |
| `heroesprofile patch-index [--write]` | Find the first Heroes Profile replay id on the latest replay's patch line. `--write` stores it as `MinReplayId` in `appsettings.json`. |
| `services start` | Start `spectate heroesprofile`, `twitch connect`, `heroesprofile download`, and `youtube uploader` as separate processes, each in its own console window. Pid files go under `%LOCALAPPDATA%\HeroesReplay\logs`. Updates the OBS collection paths first when OBS is closed. Does not start Twitch ingest. Starts the Aspire dashboard first when OTLP :4317 is not listening; a dashboard failure does not fail the services. |
| `services stop` | Write `services.stop`, wait up to 20s, kill any `heroesreplay` pid still recorded, and close Heroes of the Storm. Prints each role as `graceful`, `killed`, `already exited`, or `still running`. After every role has exited it reads OBS `GetStreamStatus` once; it never stops the stream. Exits 1 when a role is still running (that role stays in `services.json`), the game is still open, or OBS is still streaming or did not report its stream state. OBS that is closed or has no reachable websocket counts as not streaming. Do not leave the game client open after this. |
| `services status [--output text\|json]` | Classify each role from its heartbeat as `ready`, `degraded`, `stale`, `stopped`, or `failed`, with the cause and the fix, plus `status.json`. Each role rewrites its ready file every `ServiceHealth:HeartbeatInterval` (15s) with pid, path, version, readiness, `lastSuccessfulWorkAt`, and `lastError`. Stale is a live pid whose heartbeat is older than 3 intervals. Degraded is a fresh heartbeat whose last successful work is older than the role's `ServiceHealth:<Role>WorkThreshold`, or whose last error is newer than it. Failed is a recorded role that exited without a stop request. `--output json` prints `{schemaVersion, ok, code, message, environment, checkedAt, stopRequested, roles[], spectator}` with codes `service.ready`, `service.degraded`, `service.stale`, `service.stopped`, `service.failed`. Exits 1 when a role is failed, stale, or degraded. |
| `calculators coordinates [--file path]` | Parse replay, print coordinate samples, build Kill/NearEnemy/Roaming focus map |
| `calculators report [--file path]` | Spectator report for a file/directory |
| `calculators units --directory <path> [--per-map 1-5] [--output path]` | Survey a replay folder one file at a time, keep up to 5 per map, then parse those units into CSV reports |
| `check` | Runs config, heroesprofile, obs, twitch, client, battlenet, and connectivity; continues on failure; exit 1 if any fail |
| `check config` | Bind settings; print which secrets are present (never print values) |
| `check heroesprofile` | Kiota `GET /replays` max_replay_id with Bearer key |
| `check obs` | obs-websocket 5 Identify + `GetVersion`, verify scene files, and fail when the active profile or scene collection is not `OBS:ProfileName` / `OBS:SceneCollectionName` |
| `check twitch` | Helix `GetUsers` for configured channel, `GetPredictions` when predictions are enabled, and the token scopes. Fails when predictions are enabled and `GetPredictions` fails |
| `check client` | Windowed 1080p + AhliObs in Documents\Heroes of the Storm |
| `check battlenet` | Capture the Battle.net window and report the Play or Update button |
| `check connectivity` | Probe 1.1.1.1, Twitch, and Heroes Profile. Does not start an OBS stream. |
| `check timer` | Read-only scan of `HeroesOfTheStorm_x64` for a ticking match clock |
| `check twitch-extension` | Report `TwitchExtension:Enabled`, or call uploader/whoami when the extension is on |
| `client configure` | Write Variables.txt and copy AhliObs `.StormInterface`. Quit HotS first (it overwrites Variables on exit). Spectate applies this automatically if the game is not running. Windowed 1080p is required. Capture is `PrintWindow` by default (`Capture:Method`); `BitBlt` is used only when configured. |
| `client status` | Report preset mismatches |
| `client firewall` | Elevated only. Adds one inbound Windows Firewall rule per installed `Versions\Base*\HeroesOfTheStorm_x64.exe` and replaces duplicate copies. Spectate checks the rules unelevated and warns when one is missing; it never adds them itself unless it is elevated. |
| `otel up` / `otel down` / `otel status` | Standalone Aspire dashboard via the local `Aspire.Cli` tool (`dotnet tool restore`, then `dotnet aspire dashboard run --allow-anonymous`). UI http://127.0.0.1:18888, OTLP gRPC http://127.0.0.1:4317. No Docker. Spectate, Twitch, download, and YouTube each export logs, metrics, and traces under their own service name. |
| `twitch connect` | Chat, channel-point reward sync, EventSub redemptions, and Blue/Red predictions from `status.json`. Does not launch the game. Blocks. |
| `twitch say --message text` | Connect chat and send one message to the configured channel |
| `twitch rewards generate\|submit\|list\|remove-unranked-draft\|test` | Helix custom rewards. `submit` also deletes leftover Unranked Draft titles. `remove-unranked-draft` deletes only `(UD)` / `Unranked Draft` titles. `test [--title] [--message]` runs the local redeem handler |
| `twitch predictions test [--outcome Blue\|Red\|cancel]` | Create then resolve/cancel a 30s Blue/Red prediction. Default `cancel`. Any other outcome is a parse error. |
| `youtube uploader` | Watch `Data\Contexts` for `.mp4` + `youtube-entry.json`. `YouTube:DryRun` true (dev and base) writes `youtube-dry-run.json` with the planned `PublishAtUtc` and insert settings, plans in `Data\publication-reservations-dry-run.txt`, and does not call YouTube. Production (`HEROES_REPLAY_ENV=prod`) sets `DryRun` false, `YouTube:Enabled` true, and `OBS:RecordingEnabled` true, so every spectated replay is recorded from the loading screen until the MVP screen and this process uploads it. A recording is uploaded as soon as the YouTube quota allows (room for one 1600-unit insert under `YouTube:DailyQuotaUnits` in `Data\youtube-quota-units.json`, and `MaxInsertsPerQuotaDay`). Every insert is private, not made for kids, and not age-restricted. A public listing gets `publishAt` at the earliest time the `ReplayMedia` limits allow (`MaxPublicPerDay`, `MaxPublicPerWeek`, `MinimumPublicInterval`, reserved request room, map, rank, and hero cooldowns), up to `MaxPublishAhead` (7 days) ahead; a request is sent first and gets the earliest time. Only the quota, the media rules, or no time inside `MaxPublishAhead` leave a recording on disk. The mp4 is deleted on the sweep after the insert. A scheduled upload keeps `VideoId` and `PublishAtUtc` on `youtube-entry.json`; an insert that YouTube already reports public saves them on `youtube-entry-uploaded.json`. Real uploads need `Data\client_secrets.json`. `services start` launches this process. Each successful insert appends a line to `Data\youtube-library.jsonl`, which retention never deletes. It also runs the `youtube library` pass at most every `YouTube:LibraryInterval` (1 hour), and it is the only process that calls YouTube; the spectator's duplicate check reads `Data\youtube-replay-ids.txt` and the receipts only. |
| `youtube library [--once]` | Run the uploader's library pass now. Lists the channel's uploads with the `{ChannelId}:library` consent (full `https://www.googleapis.com/auth/youtube` scope), adds every replay id to `Data\youtube-replay-ids.txt`, records videos missing from `Data\youtube-library.jsonl` (Heroes Profile fills a missing map, mode, rank, or build; unresolved videos retry with backoff), and files each public video, newest first, into the groups `YouTube:Playlists` turns on: `Map` (`Alterac Pass`, every mode), `Mode` (`Storm League`, `Quick Match`, `ARAM`), `Rank` (`Storm League - Diamond`, division dropped), `Draft` (`Unusual drafts - Double healer`, one per `Draft:` note), `ViewerReview` (`Viewer requested reviews`, a request that named a player, i.e. a `Featured:` line), and `Patch` (`Patch {line}`, or the season name, for the current line; `Patch {line} archive` for older lines). All six are on by default; `MapMode` (the earlier `{Map} - Storm League - {League}` playlists) is off. Clips go into the patch playlist only; old-template videos have no draft note or request, so they get map, mode, rank, and patch. Each insert is 50 units: 4 (200 units) for a ranked Storm League video with the defaults, +1 per draft note, +1 for a review. Prints the planned inserts. `--once` runs one pass and exits; without it the pass repeats each `YouTube:LibraryInterval` until stopped. Shares the uploader's daily units in `Data\youtube-quota-units.json` (`LibraryUnitsPerDay` 3000, `DailyQuotaUnits` 10000 minus `QuotaReserveUnits` 1600) and its lock, so the two cannot double-spend. A quota response pauses the pass until the next Pacific day. Not started by `services start`; `youtube uploader` runs the same pass there. Dry-run writes `Data\youtube-library-dry-run.json` (the planned inserts, videos per playlist, and `InsertUnits`) and calls neither YouTube nor Heroes Profile. Playlist ids are cached in `Data\youtube-playlists.json`. |
| `obs arm` | Allow this machine to start Twitch ingest: writes `%LOCALAPPDATA%\HeroesReplay\stream-armed`. Ingest needs this arm **and** `OBS:StreamingEnabled` (prod). Stream PC only; never on ASA-SERVER. The spectator picks it up on its next reconcile. |
| `obs disarm` | Delete the arm. No new stream starts; a live stream keeps running until `services stop` or OBS stops it. |
| `obs status` | Print the arm, `OBS:StreamingEnabled`, and the expected `OBS:ProfileName` / `OBS:SceneCollectionName`. Does not connect to OBS and does not resolve secrets. |
| `update check` | Print the installed version and the latest release tag. Does not download or restart. |
| `update preserve-min-replay-id` / `update release-health` | Called by `apply-release.ps1`: keep the higher `MinReplayId` when a release replaces `appsettings.json`, and exit 0 once `role-ready.txt` shows every role ready for the stabilization window. |
| `update migrate-stream-arm --previous <dir> [--environment env]` / `update install-obs --install <dir> [--environment env]` | Called by `apply-release.ps1`. The first arms the machine once when the replaced install's effective `OBS:StreamingEnabled` is true (never when false, never twice). The second copies the scene collection while OBS is closed and installs the profile template only when the machine has no profile. Do not run them by hand on a dev box: they write `%LOCALAPPDATA%\HeroesReplay` and `%APPDATA%\obs-studio`. |
| `mcp` | Stdio MCP server. Logs on stderr. Snapshot: `%LOCALAPPDATA%/HeroesReplay/status.json`. Read-only OBS tools: `obs_inspect`, `obs_validate`, `obs_screenshot` |

New commands go on `HeroesReplayCommand` and need a **Smoke** test in `src/HeroesReplay.Tests/Smoke`.

## Secrets

Skill `op-service-account`. Clone to `C:\heroesreplay\HeroesReplay`. `pwsh -File tools/bootstrap-workstation.ps1` creates Data/Replays dirs, copies the OBS collection, installs the OBS profile template only when none exists, fills secrets, installs git hooks. Live file `src/HeroesReplay.CLI/appsettings.secrets.json` (gitignored). `BindSettings` runs `SecretResolver.Apply`. Env prefix `HEROES_REPLAY_`. Do not log resolved tokens. Layout: `AGENTS.md` Environments.

## Config load

`GetConfiguration` uses the current directory if `appsettings.json` is there, otherwise `AppContext.BaseDirectory` (exe output). `check` must work from the repo root.

## Tests vs CLI

| Filter | What |
| --- | --- |
| default / `Category=Unit` | Everything under `src/HeroesReplay.Tests/Unit` (one folder per Core slice) |
| `Category=Smoke` | Parse `--help`; asserts root, `check`, `client`, `otel`, `services`, `heroesprofile`, `twitch`, `calculators`, `youtube`, `obs`, and `update` subcommands exist. Exit codes: `spectate --help` and `spectate file --help` exit 0 without elevation; an invalid `--player` or a missing `--file` exits 1 |
| `Category=Integration` | Live Heroes Profile v1 list/download (needs `op` or env key), YouTube dry-run upload, medium-integrity process launch |

After changing a check target, run that CLI command, not only unit tests.

## MCP (agent monitor)

Repo `.grok/config.toml` registers `heroesreplay`. Tools:

- `get_spectator_status` — phase, timer, replay, focus, OBS session, game process, stale flag
- `check_battlenet` — Play or Update on the Battle.net window
- `get_current_focus` — selected hero
- `check_config` / `check_heroesprofile` / `check_obs` / `check_twitch` — JSON of the CLI checks
- `obs_inspect` — live OBS, read-only (structured content, `schemaVersion` 1): versions and `availableRequestCount`, active profile and collection against `OBS:ProfileName` / `OBS:SceneCollectionName`, video base/output size and FPS, program scene, scenes with items (name, kind, enabled), inputs with kind, mute, and volume (`globalAudio` is `desktop1`, `mic1`, … for the global devices), stream and record status, `GetStats` (render and encoding lag), `streamService` (`type` and `keySet` only), and `streamArm` (`armed`, `streamingEnabled`, `mayStart`, `blockedBy`). A failed request is listed in `unread`
- `obs_validate` — the loaded collection against the packaged `obs/Default.json` contract (`ObsContract`: game, waiting, and report scenes, the info, tier, and rank sources in the game scene, each report browser source). `findings` have a `code`, `severity` (`error` fails `ok`, `warning` does not), `subject`, and `message`. Codes: `obs.profile_mismatch`, `obs.collection_mismatch`, `obs.selection_unreadable`, `obs.scene_missing`, `obs.source_missing`, `obs.source_kind_mismatch`, `obs.scene_item_missing`, `obs.file_missing`, `obs.url_invalid`, `obs.mic_enabled`, `obs.request_unavailable`, `obs.asset_missing`, `obs.bundle_missing`, `obs.bundle_invalid` (errors); `obs.runtime_file_missing` (a file under `Location:DataDirectory` the spectator writes), `obs.path_stale` (exists, but this install's rewrite points elsewhere), `obs.mic_muted`, `obs.collection_custom` (warnings). Web URLs are checked for form only and never echoed
- `obs_screenshot` — `source` (default: program scene) and `width` (default 960, capped at 1920); returns a real MCP `image/png` block plus one text line

The OBS tools send only the Get requests in `ObsReadOnly` and resolve only `OBS:WebSocketPassword`. Each call opens its own short session (3 s identify timeout), reads, and disconnects; the spectator's one session per replay is not touched. When OBS is closed they return `ok: false` with `code: obs.unreachable`; other tool-level codes are `obs.auth_failed`, `obs.password_unresolved`, `obs.settings_unreadable`, `obs.request_failed`, and `obs.source_not_found` (screenshot of an unknown source). `obs_screenshot` returns `isError` with `code: message`. No MCP tool changes OBS: fixes go through guarded CLI commands (`obs arm` / `obs disarm`, later `obs plan` / `apply`). The stream key never leaves `ObsStreamService.Summarize`.

The MCP process is **not** the spectator. Run `spectate file` (or heroesprofile) separately; it writes the status file every second. If `updatedAt` is older than 15s, `snapshotStale` is true and `spectatorRunning` is false.

A local end-to-end run is not done until both patch launches have reached the match clock. The newest installed `Versions\Base*` exe signs in through Battle.net before the replay file is opened. An older installed exe is started by HeroesSwitcher with the replay file, not by Battle.net Play. A `Base*` folder with no executable is missing and must not launch the current client. See `AGENTS.md` "Current patch and previous patch".

Do not write to stdout from MCP tools (stdio is JSON-RPC).
