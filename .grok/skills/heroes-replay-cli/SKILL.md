---
name: heroes-replay-cli
description: >
  heroesreplay CLI: spectate, calculators, check, client, otel, twitch, youtube, secrets, op://.
  Use when adding or changing commands, validating integrations, running the exe,
  or /heroes-replay-cli.
---

# heroesreplay CLI

Entry: `src/HeroesReplay.CLI`. Assembly name `heroesreplay`. System.CommandLine 2 (`Subcommands`, `SetAction`).

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- <command>
```

`--no-launch-profile` is required; launchSettings would otherwise inject leftover spectate args.

**ASA-SERVER** is for developing and proving the spectator, CLI, and services. The phases are in `AGENTS.md` (unit, then build, then one replay, then 1–5 full games only when the loop itself changed). Stop with `heroesreplay services stop`, which also closes Heroes of the Storm. If you kill `heroesreplay` yourself, close the game too (`CloseMainWindow`, then `Kill` if it does not exit). An open client with no spectator is a stuck replay. Do not start Twitch ingest. **DESKTOP-8SJE72** is the production spectate and live stream. Do not kill or rebuild it.

## Commands

| Command | Behavior |
| --- | --- |
| `spectate file [--file path]` | Play one `.StormReplay` or each file in a directory, then exit. Starts the Aspire dashboard when OTLP :4317 is down; dashboard failure does not fail the replay. |
| `spectate heroesprofile` | Play `.StormReplay` files already in `Data\Standard` and `Data\Requests`. Does not call Heroes Profile. Same dashboard startup as `spectate file`. |
| `heroesprofile download` | List and download Storm League replays into `Data\Standard` and `Data\Requests`. Does not launch the game. |
| `services start` | Start spectate, `twitch connect`, `heroesprofile download`, and `youtube uploader` as separate processes. Logs under `%LOCALAPPDATA%\HeroesReplay\logs`. Does not start Twitch ingest. Starts the Aspire dashboard first when OTLP :4317 is not listening; a dashboard failure does not fail the services. |
| `services stop` | Write `services.stop`, wait up to 20s, kill any `heroesreplay` pid still recorded, and close Heroes of the Storm. Do not leave the game client open after this. |
| `services status` | Which of those processes are still alive, plus `status.json`. |
| `calculators coordinates [--file path]` | Parse replay, print coordinate samples, build Kill/NearEnemy/Roaming focus map |
| `calculators report [--file path]` | Spectator report for a file/directory |
| `calculators units --directory <path> [--per-map 1-5] [--output path]` | Survey a replay folder one file at a time, keep up to 5 per map, then parse those units into CSV reports |
| `check` | Config + Heroes Profile + OBS + Twitch; continues on failure; exit 1 if any fail |
| `check config` | Bind settings; print which secrets are present (never print values) |
| `check heroesprofile` | Kiota `GET /replays` max_replay_id with Bearer key |
| `check obs` | obs-websocket 5 Identify + `GetVersion` |
| `check twitch` | Helix `GetUsers` for configured channel |
| `check client` | Windowed 1080p + AhliObs in Documents\Heroes of the Storm |
| `client configure` | Write Variables.txt and copy AhliObs `.StormInterface`. Quit HotS first (it overwrites Variables on exit). Spectate applies this automatically if the game is not running. Capture is GDI BitBlt; windowed 1080p is required. |
| `client status` | Report preset mismatches |
| `otel up` / `otel down` / `otel status` | Standalone Aspire dashboard via the local `Aspire.Cli` tool (`dotnet tool restore`, then `dotnet aspire dashboard run --allow-anonymous`). UI http://127.0.0.1:18888, OTLP gRPC http://127.0.0.1:4317. No Docker. Spectate, Twitch, download, and YouTube each export logs, metrics, and traces under their own service name. |
| `twitch connect` | Chat, PubSub, and Blue/Red predictions from `status.json`. Does not launch the game. Blocks. |
| `twitch rewards generate\|submit\|list\|remove-unranked-draft\|test` | Helix custom rewards. `submit` also deletes leftover Unranked Draft titles. `remove-unranked-draft` deletes only `(UD)` / `Unranked Draft` titles. `test` runs the local redeem handler |
| `twitch predictions test [--outcome Blue\|Red\|cancel]` | Create then resolve/cancel a 30s Blue/Red prediction |
| `youtube uploader` | Watch `Data\\Contexts` for `.mp4` + `youtube-entry.json`. `YouTube:DryRun` true (dev and base) writes `youtube-dry-run.json` and does not call YouTube. Production (`HEROES_REPLAY_ENV=prod`) sets `DryRun` false, `YouTube:Enabled` true, and `OBS:RecordingEnabled` true, so every spectated replay is recorded from the loading screen until the MVP screen and this process uploads it. A completed upload saves `VideoId` on `youtube-entry-uploaded.json`. Real uploads need `Data\\client_secrets.json`. `services start` launches this process. |
| `youtube library` | File those videos into `{Map} - {Mode}` playlists (`{Map} - Storm League - {League}` for ranked games, division dropped). Polls every 60 seconds until stopped. `--once` exits after one pass. Not part of `services start`. Dry-run writes `Data\\youtube-library-dry-run.json` and does not call YouTube. A real pass needs the OAuth scope `https://www.googleapis.com/auth/youtube` and stores playlist ids in `Data\\youtube-playlists.json`. |
| `mcp` | Stdio MCP server. Logs on stderr. Snapshot: `%LOCALAPPDATA%/HeroesReplay/status.json` |

New commands go on `HeroesReplayCommand` and need a **Smoke** test in `src/HeroesReplay.Tests/Smoke`.

## Secrets

Skill `op-service-account`. Clone to `C:\heroesreplay\HeroesReplay`. `pwsh -File tools/bootstrap-workstation.ps1` creates Data/Replays dirs, copies OBS collection, fills secrets. Live file `src/HeroesReplay.CLI/appsettings.secrets.json` (gitignored). `BindSettings` runs `SecretResolver.Apply`. Env prefix `HEROES_REPLAY_`. Do not log resolved tokens. Layout: `AGENTS.md` Environments.

## Config load

`GetConfiguration` uses the current directory if `appsettings.json` is there, otherwise `AppContext.BaseDirectory` (exe output). `check` must work from the repo root.

## Tests vs CLI

| Filter | What |
| --- | --- |
| default / `Category=Unit` | Analysis only |
| `Category=Smoke` | Parse `--help`; asserts `check` subcommands exist |
| `Category=Integration` | Live Heroes Profile v1 list/download (needs `op` or env key) |

After changing a check target, run that CLI command, not only unit tests.

## MCP (agent monitor)

Repo `.grok/config.toml` registers `heroesreplay`. Tools:

- `get_spectator_status` — phase, timer, replay, focus, OBS session, game process, stale flag
- `get_current_focus` — selected hero
- `check_config` / `check_heroesprofile` / `check_obs` / `check_twitch` — JSON of the CLI checks

The MCP process is **not** the spectator. Run `spectate file` (or heroesprofile) separately; it writes the status file every second. If `updatedAt` is older than 15s, `snapshotStale` is true and `spectatorRunning` is false.

Do not write to stdout from MCP tools (stdio is JSON-RPC).
