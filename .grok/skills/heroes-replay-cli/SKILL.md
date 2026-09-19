---
name: heroes-replay-cli
description: >
  heroesreplay CLI: spectate, calculators, check, client, twitch, youtube, secrets, op://.
  Use when adding or changing commands, validating integrations, running the exe,
  or /heroes-replay-cli.
---

# heroesreplay CLI

Entry: `src/HeroesReplay.CLI`. Assembly name `heroesreplay`. System.CommandLine 2 (`Subcommands`, `SetAction`).

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- <command>
```

`--no-launch-profile` is required; launchSettings would otherwise inject leftover spectate args.

## Commands

| Command | Behavior |
| --- | --- |
| `spectate file [--file path]` | Play one `.StormReplay` or each file in a directory, then exit |
| `spectate heroesprofile` | Loop: download Storm League replays from Heroes Profile S3 |
| `calculators coordinates [--file path]` | Parse replay, print coordinate samples, build Kill/NearEnemy/Roaming focus map |
| `calculators report [--file path]` | Spectator report for a file/directory |
| `check` | Config + Heroes Profile + OBS + Twitch; continues on failure; exit 1 if any fail |
| `check config` | Bind settings; print which secrets are present (never print values) |
| `check heroesprofile` | `Replay/Max` with the resolved API key |
| `check obs` | obs-websocket 5 Identify + `GetVersion` |
| `check twitch` | Helix `GetUsers` for configured channel |
| `check client` | Windowed 1080p + AhliObs in Documents\Heroes of the Storm |
| `client configure` | Write Variables.txt and copy AhliObs `.StormInterface`. Quit HotS first (it overwrites Variables on exit). Spectate applies this automatically if the game is not running. Capture is GDI BitBlt; windowed 1080p is required. |
| `client status` | Report preset mismatches |
| `twitch connect` | Chat bot; blocks |
| `twitch rewards generate\|submit` | Helix custom rewards |
| `youtube uploader` | Upload OBS recordings |
| `mcp` | Stdio MCP server. Logs on stderr. Snapshot: `%LOCALAPPDATA%/HeroesReplay/status.json` |

New commands go on `HeroesReplayCommand` and need a **Smoke** test in `src/HeroesReplay.Tests/Smoke`.

## Secrets

- Live file: `src/HeroesReplay.CLI/appsettings.secrets.json` (gitignored).
- Template: `appsettings.secrets.example.json`.
- Env prefix: `HEROES_REPLAY_` (e.g. `HEROES_REPLAY_HeroesProfileApi__ApiKey`).
- Values starting with `op://` are resolved by `SecretResolver` via `op read`. Heroes Profile default URI: `op://Private/Heroes Profile API/password`.
- `BindSettings` runs `SecretResolver.Apply` after JSON bind. Do not log resolved tokens.

## Config load

`GetConfiguration` uses the current directory if `appsettings.json` is there, otherwise `AppContext.BaseDirectory` (exe output). `check` must work from the repo root.

## Tests vs CLI

| Filter | What |
| --- | --- |
| default / `Category=Unit` | Analysis only |
| `Category=Smoke` | Parse `--help`; asserts `check` subcommands exist |
| `Category=Integration` | Live Heroes Profile `Replay/Max` (needs `op` or env key) |

After changing a check target, run that CLI command, not only unit tests.

## MCP (agent monitor)

Repo `.grok/config.toml` registers `heroesreplay`. Tools:

- `get_spectator_status` — phase, timer, replay, focus, OBS session, game process, stale flag
- `get_current_focus` — selected hero
- `check_config` / `check_heroesprofile` / `check_obs` / `check_twitch` — JSON of the CLI checks

The MCP process is **not** the spectator. Run `spectate file` (or heroesprofile) separately; it writes the status file every second. If `updatedAt` is older than 15s, `snapshotStale` is true and `spectatorRunning` is false.

Do not write to stdout from MCP tools (stdio is JSON-RPC).
