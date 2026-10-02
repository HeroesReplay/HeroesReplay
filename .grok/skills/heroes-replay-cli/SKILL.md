---
name: heroes-replay-cli
description: >
  heroesreplay CLI: spectate, services, heroesprofile, calculators, check, client, otel, twitch,
  youtube, update, mcp, secrets, op://.
  Use when adding or changing commands, validating integrations, running the exe,
  or /heroes-replay-cli.
---

# heroesreplay CLI

Entry: `src/HeroesReplay.CLI`. Assembly name `heroesreplay`. System.CommandLine 2 (`Subcommands`, `SetAction`). `spectate` and its subcommands, including `--help`, exit 1 unless the process is elevated.

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- <command>
```

`--no-launch-profile` is required; launchSettings would otherwise inject leftover spectate args.

**ASA-SERVER** is for developing and proving the spectator, CLI, and services. The phases are in `AGENTS.md` (unit, then build, then one replay, then 1–5 full games only when the loop itself changed). Stop with `heroesreplay services stop`, which also closes Heroes of the Storm. If you kill `heroesreplay` yourself, close the game too (`CloseMainWindow`, then `Kill` if it does not exit). An open client with no spectator is a stuck replay. Do not start Twitch ingest. **DESKTOP-8SJEK72** is the production spectate and live stream. Do not kill or rebuild it.

## Commands

| Command | Behavior |
| --- | --- |
| `spectate file [--file path] [--player 0-9]` | Play one `.StormReplay` or each file in a directory, then exit. `--file` defaults to `Location:ReplaySource`. `--player` follows that hero (1-9, or 0 for the tenth) while it is alive. Starts the Aspire dashboard when OTLP :4317 is down; dashboard failure does not fail the replay. |
| `spectate heroesprofile` | Play `.StormReplay` files already in `Data\Standard` and `Data\Requests`. Does not call Heroes Profile. Same dashboard startup as `spectate file`. |
| `heroesprofile download` | List and download Storm League replays into `Data\Standard`, and requested replays into `Data\Requests`. Does not launch the game. |
| `heroesprofile patch-index [--write]` | Find the first Heroes Profile replay id on the latest replay's patch line. `--write` stores it as `MinReplayId` in `appsettings.json`. |
| `services start` | Start `spectate heroesprofile`, `twitch connect`, `heroesprofile download`, and `youtube uploader` as separate processes, each in its own console window. Pid files go under `%LOCALAPPDATA%\HeroesReplay\logs`. Updates the OBS collection paths first when OBS is closed. Does not start Twitch ingest. Starts the Aspire dashboard first when OTLP :4317 is not listening; a dashboard failure does not fail the services. |
| `services stop` | Write `services.stop`, wait up to 20s, kill any `heroesreplay` pid still recorded, and close Heroes of the Storm. Do not leave the game client open after this. |
| `services status` | Which of those processes are still alive, plus `status.json`. |
| `calculators coordinates [--file path]` | Parse replay, print coordinate samples, build Kill/NearEnemy/Roaming focus map |
| `calculators report [--file path]` | Spectator report for a file/directory |
| `calculators units --directory <path> [--per-map 1-5] [--output path]` | Survey a replay folder one file at a time, keep up to 5 per map, then parse those units into CSV reports |
| `check` | Runs config, heroesprofile, obs, twitch, client, battlenet, and connectivity; continues on failure; exit 1 if any fail |
| `check config` | Bind settings; print which secrets are present (never print values) |
| `check heroesprofile` | Kiota `GET /replays` max_replay_id with Bearer key |
| `check obs` | obs-websocket 5 Identify + `GetVersion`, and verify scene files |
| `check twitch` | Helix `GetUsers` for configured channel, `GetPredictions` when predictions are enabled, and the token scopes |
| `check client` | Windowed 1080p + AhliObs in Documents\Heroes of the Storm |
| `check battlenet` | Capture the Battle.net window and report the Play or Update button |
| `check connectivity` | Probe 1.1.1.1, Twitch, and Heroes Profile. Does not start an OBS stream. |
| `check timer` | Read-only scan of `HeroesOfTheStorm_x64` for a ticking match clock |
| `check twitch-extension` | Report `TwitchExtension:Enabled`, or call uploader/whoami when the extension is on |
| `client configure` | Write Variables.txt and copy AhliObs `.StormInterface`. Quit HotS first (it overwrites Variables on exit). Spectate applies this automatically if the game is not running. Windowed 1080p is required. Capture is `PrintWindow` by default (`Capture:Method`); `BitBlt` is used only when configured. |
| `client status` | Report preset mismatches |
| `otel up` / `otel down` / `otel status` | Standalone Aspire dashboard via the local `Aspire.Cli` tool (`dotnet tool restore`, then `dotnet aspire dashboard run --allow-anonymous`). UI http://127.0.0.1:18888, OTLP gRPC http://127.0.0.1:4317. No Docker. Spectate, Twitch, download, and YouTube each export logs, metrics, and traces under their own service name. |
| `twitch connect` | Chat, channel-point reward sync, EventSub redemptions, and Blue/Red predictions from `status.json`. Does not launch the game. Blocks. |
| `twitch say --message text` | Connect chat and send one message to the configured channel |
| `twitch rewards generate\|submit\|list\|remove-unranked-draft\|test` | Helix custom rewards. `submit` also deletes leftover Unranked Draft titles. `remove-unranked-draft` deletes only `(UD)` / `Unranked Draft` titles. `test [--title] [--message]` runs the local redeem handler |
| `twitch predictions test [--outcome Blue\|Red\|cancel]` | Create then resolve/cancel a 30s Blue/Red prediction. Default `cancel`. |
| `youtube uploader` | Watch `Data\Contexts` for `.mp4` + `youtube-entry.json`. `YouTube:DryRun` true (dev and base) writes `youtube-dry-run.json` and does not call YouTube. Production (`HEROES_REPLAY_ENV=prod`) sets `DryRun` false, `YouTube:Enabled` true, and `OBS:RecordingEnabled` true, so every spectated replay is recorded from the loading screen until the MVP screen and this process uploads it. Public uploads are paced by the `ReplayMedia` limits (`MaxPublicPerDay`, `MaxPublicPerWeek`, `MinimumPublicInterval`). A completed upload saves `VideoId` on `youtube-entry-uploaded.json`. Real uploads need `Data\client_secrets.json`. `services start` launches this process. It also runs a `youtube library` pass at startup and after each upload drain. |
| `youtube library [--once]` | File public videos into `{Map} - {Mode}` playlists (`{Map} - Storm League - {League}` for ranked games, division dropped) and patch playlists (`Patch {line}`, or the season name, for the current line; `Patch {line} archive` for older lines). Polls every 60 seconds until stopped. `--once` exits after one pass. Not started by `services start`, but `youtube uploader` runs the same pass there. Dry-run writes `Data\youtube-library-dry-run.json` and does not call YouTube. A real pass needs the OAuth scope `https://www.googleapis.com/auth/youtube` and stores playlist ids in `Data\youtube-playlists.json`. |
| `update check` | Print the installed version and the latest release tag. Does not download or restart. |
| `update preserve-min-replay-id` / `update release-health` | Called by `apply-release.ps1`: keep the higher `MinReplayId` when a release replaces `appsettings.json`, and exit 0 once `role-ready.txt` shows every role ready for the stabilization window. |
| `mcp` | Stdio MCP server. Logs on stderr. Snapshot: `%LOCALAPPDATA%/HeroesReplay/status.json` |

New commands go on `HeroesReplayCommand` and need a **Smoke** test in `src/HeroesReplay.Tests/Smoke`.

## Secrets

Skill `op-service-account`. Clone to `C:\heroesreplay\HeroesReplay`. `pwsh -File tools/bootstrap-workstation.ps1` creates Data/Replays dirs, copies OBS collection, fills secrets, installs git hooks. Live file `src/HeroesReplay.CLI/appsettings.secrets.json` (gitignored). `BindSettings` runs `SecretResolver.Apply`. Env prefix `HEROES_REPLAY_`. Do not log resolved tokens. Layout: `AGENTS.md` Environments.

## Config load

`GetConfiguration` uses the current directory if `appsettings.json` is there, otherwise `AppContext.BaseDirectory` (exe output). `check` must work from the repo root.

## Tests vs CLI

| Filter | What |
| --- | --- |
| default / `Category=Unit` | Everything under `src/HeroesReplay.Tests/Unit` (one folder per Core slice) |
| `Category=Smoke` | Parse `--help`; asserts root, `check`, `client`, `otel`, `services`, `heroesprofile`, `twitch`, `calculators`, and `youtube` subcommands exist |
| `Category=Integration` | Live Heroes Profile v1 list/download (needs `op` or env key), YouTube dry-run upload, medium-integrity process launch |

After changing a check target, run that CLI command, not only unit tests.

## MCP (agent monitor)

Repo `.grok/config.toml` registers `heroesreplay`. Tools:

- `get_spectator_status` — phase, timer, replay, focus, OBS session, game process, stale flag
- `check_battlenet` — Play or Update on the Battle.net window
- `get_current_focus` — selected hero
- `check_config` / `check_heroesprofile` / `check_obs` / `check_twitch` — JSON of the CLI checks

The MCP process is **not** the spectator. Run `spectate file` (or heroesprofile) separately; it writes the status file every second. If `updatedAt` is older than 15s, `snapshotStale` is true and `spectatorRunning` is false.

A local end-to-end run is not done until both patch launches have reached the match clock. The newest installed `Versions\Base*` exe signs in through Battle.net before the replay file is opened. An older installed exe is started by HeroesSwitcher with the replay file, not by Battle.net Play. A `Base*` folder with no executable is missing and must not launch the current client. See `AGENTS.md` "Current patch and previous patch".

Do not write to stdout from MCP tools (stdio is JSON-RPC).
