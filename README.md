# Heroes Replay

Automated spectator for Heroes of the Storm `.StormReplay` files. It parses a replay, scores who to watch each second, launches the game, drives camera focus, and can switch OBS Studio scenes while streaming.

Originally built for [twitch.tv/saltysadism](https://twitch.tv/saltysadism). Modernized to **.NET 10 LTS**.

## What it does

1. Load a local `.StormReplay`, a directory of them, or download Storm League games from Heroes Profile (S3).
2. Build a **focus timeline**: kills, proximity, camps, objectives, structures, emotes. Weights live in `appsettings.json`.
3. Launch Heroes of the Storm (Battle.net when the replay is the latest client build), wait for the in-game timer (WinRT OCR + **BitBlt** of the windowed client).
4. Send spectator hotkeys (`1`–`0`, Ctrl+panels) as the OCR timer advances. After load, send **Ctrl+T** once so the replay timeline is hidden.
5. Optionally control **OBS Studio 28+** (obs-websocket **5**, default `ws://127.0.0.1:4455`): game scene, recording folder, rank images, post-game report scenes.
6. Chat `!stats` shows the observer stats panel for 10 seconds (2 minute cooldown). Talents still open automatically.

## Requirements

- Windows 10/11 (OCR, **BitBlt**, process control). The client must be **windowed 1080p** (`heroesreplay client configure`). Fullscreen D3D11 is not supported.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Heroes of the Storm + Battle.net
- Optional: OBS Studio 28+ with **Tools → WebSocket Server Settings** enabled (port **4455**)
- Optional: Twitch app credentials, Heroes Profile API key, AWS keys for S3 replay download

## Build

```powershell
git clone https://github.com/HeroesReplay/HeroesReplay.git
cd HeroesReplay
dotnet tool restore
dotnet build heroes-replay.slnx
dotnet test heroes-replay.slnx
```

Tests are split by `Category` trait. **`dotnet test` runs Unit only.**

```powershell
dotnet test heroes-replay.slnx                              # Unit (every change)
dotnet test heroes-replay.slnx -p:TestCategory=Integration  # Heroes Profile API (needs op/env)
dotnet test heroes-replay.slnx -p:TestCategory=Smoke        # CLI help / command surface
dotnet test heroes-replay.slnx --filter Category=Integration
```

Copy `src/HeroesReplay.CLI/appsettings.secrets.example.json` to `appsettings.secrets.json`. The Heroes Profile key can be a 1Password reference (`op://…`); the CLI resolves it with `op read` when you are signed in.

Format / lint (CSharpier):

```powershell
dotnet csharpier format src
dotnet csharpier check src
# or fail the build if formatting drifted:
dotnet build heroes-replay.slnx -p:CSharpierCheck=true
```

## Run

```powershell
cd src/HeroesReplay.CLI
dotnet run --no-launch-profile -- --help
```

| Command | Purpose |
| --- | --- |
| `spectate file --file <path>` | Play one replay (or each file in a directory) then exit |
| `spectate heroesprofile` | Download and spectate Heroes Profile S3 replays in a loop |
| `calculators coordinates` | Parse the newest Documents replay and prove coordinates + focus timeline |
| `calculators report --file <path>` | Write a spectator report |
| `twitch connect` / `twitch rewards …` | Chat bot and channel-point rewards |
| `youtube uploader` | Upload OBS recordings |
| `client configure` | Windowed 1080p + AhliObs in `Documents\Heroes of the Storm` (Variables.txt + StormInterface). Quit the game first. |
| `client status` / `check client` | Verify that preset |
| `check` | Config + Heroes Profile + OBS + Twitch + client (continues on failure) |
| `check config` / `check heroesprofile` / `check obs` / `check twitch` / `check client` | One integration at a time |
| `mcp` | Stdio MCP server for agents (`get_spectator_status`, checks). Pair with a running `spectate` process. |

Grok picks up the server from `.grok/config.toml` (`mcp_servers.heroesreplay`). Status snapshot: `%LOCALAPPDATA%\HeroesReplay\status.json`.

Secrets go in `src/HeroesReplay.CLI/appsettings.secrets.json` (not committed). Environment variables use the prefix `HEROES_REPLAY_`. Set `HEROES_REPLAY_ENV` to `dev` or `prod` to layer `appsettings.{env}.json`.

OBS password:

```json
"OBS": {
  "Enabled": true,
  "WebSocketEndpoint": "ws://127.0.0.1:4455",
  "WebSocketPassword": ""
}
```

## Solution layout

| Project | Role |
| --- | --- |
| `src/HeroesReplay.CLI` | `heroesreplay` executable, System.CommandLine 2 |
| `src/HeroesReplay.Core` | Analysis, game/OBS/Twitch/YouTube |
| `src/HeroesReplay.Tests` | xUnit |

The solution file is **`heroes-replay.slnx`** (XML). Do not add a parallel `.sln`.

## Agent instructions

See [AGENTS.md](AGENTS.md). Repo skills live in [`.grok/skills/`](.grok/skills/).

## License

MIT. Copyright (c) Patrick Magee.
