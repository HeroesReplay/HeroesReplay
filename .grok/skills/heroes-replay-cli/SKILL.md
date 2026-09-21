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

On **ASA-SERVER** (dev VM): after spectator/OCR/OBS/Twitch code changes, stop `heroesreplay`, `dotnet build heroes-replay.slnx -c Release`, copy `appsettings.secrets.json` into the CLI Release bin, and relaunch. That is the normal test loop. Do **not** start Twitch ingest. On **DESKTOP-8SJE72** (live), do not kill/rebuild the running stream.

## Commands

| Command | Behavior |
| --- | --- |
| `spectate file [--file path]` | Play one `.StormReplay` or each file in a directory, then exit |
| `spectate heroesprofile` | Play `.StormReplay` files already in `Data\Standard` and `Data\Requests`. Does not call Heroes Profile. |
| `heroesprofile download` | List and download Storm League replays into `Data\Standard` and `Data\Requests`. Does not launch the game. |
| `services start` | Start spectate, `twitch connect`, `heroesprofile download`, and `youtube uploader` as separate processes. Logs under `%LOCALAPPDATA%\HeroesReplay\logs`. Does not start Twitch ingest. |
| `services stop` | Kill the `heroesreplay` pids recorded by `services start`. Does not close Heroes of the Storm. |
| `services status` | Which of those processes are still alive, plus `status.json`. |
| `calculators coordinates [--file path]` | Parse replay, print coordinate samples, build Kill/NearEnemy/Roaming focus map |
| `calculators report [--file path]` | Spectator report for a file/directory |
| `check` | Config + Heroes Profile + OBS + Twitch; continues on failure; exit 1 if any fail |
| `check config` | Bind settings; print which secrets are present (never print values) |
| `check heroesprofile` | Kiota `GET /replays` max_replay_id with Bearer key |
| `check obs` | obs-websocket 5 Identify + `GetVersion` |
| `check twitch` | Helix `GetUsers` for configured channel |
| `check client` | Windowed 1080p + AhliObs in Documents\Heroes of the Storm |
| `client configure` | Write Variables.txt and copy AhliObs `.StormInterface`. Quit HotS first (it overwrites Variables on exit). Spectate applies this automatically if the game is not running. Capture is GDI BitBlt; windowed 1080p is required. |
| `client status` | Report preset mismatches |
| `otel up` / `otel down` / `otel status` | Optional Aspire Dashboard via Docker Compose (`deploy/aspire/docker-compose.yml`). UI http://127.0.0.1:18888, OTLP gRPC :4317. |
| `twitch connect` | Chat, PubSub, and Blue/Red predictions from `status.json`. Does not launch the game. Blocks. |
| `twitch rewards generate\|submit\|list\|test` | Helix custom rewards; `test` runs the local redeem handler |
| `twitch predictions test [--outcome Blue\|Red\|cancel]` | Create then resolve/cancel a 30s Blue/Red prediction |
| `youtube uploader` | Watch `Data\\Contexts` for `.mp4` + `youtube-entry.json`. Needs Google `client_secrets.json`. Spectate only writes the entry / records for **requested** replays unless `YouTube:Enabled` / `OBS:RecordingEnabled`. |
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
