# Heroes Replay

Automated spectator for Heroes of the Storm `.StormReplay` files. It parses a replay, scores who to watch each second, launches the game, drives camera focus, and can switch OBS Studio scenes while streaming.

Originally built for [twitch.tv/saltysadism](https://twitch.tv/saltysadism). Modernized to **.NET 10 LTS**. The product vision and the 24/7 cycle are in [docs/vision.md](docs/vision.md). What the YouTube uploader records, publishes, and files is in [docs/youtube-uploader.md](docs/youtube-uploader.md).

`develop` is the default branch. A clone checks it out, and day-to-day work lands there. `master` is only the production branch. Merging into `master` runs the release workflow and publishes `heroesreplay-win-x64.zip`: the executable, `appsettings.json` with its `dev` and `prod` overlays, and the OBS scene collection with its assets. The stream PC installs that zip. It does not build from this checkout.

## What it does

1. Load a local `.StormReplay`, a directory of them, or download Storm League games from Heroes Profile.
2. Build a **focus timeline**: kills, proximity, camps, objectives, structures, emotes. Weights live in `appsettings.json`.
3. Launch Heroes of the Storm (Battle.net when the replay is the latest client build, HeroesSwitcher for an older installed build), wait for the match clock (read from game memory, with WinRT OCR of a **PrintWindow** capture of the windowed client as the fallback).
4. Send spectator hotkeys (`1`–`0`, Ctrl+panels) as the match clock advances. Player focus uses Observe Player 1–10, not Follow Player Camera (`C`) and not Shift+Z ultra zoom (that jittered on hero swaps). AhliObs already hides the replay control panel; do not send Ctrl+Shift+O (that chord toggles it back on).
5. Optionally control **OBS Studio 30.0+** (obs-websocket **5.3+**, default `ws://127.0.0.1:4455`): game scene, recording folder, rank images, post-game report scenes (`match-report` for the Heroes Profile match page, `prediction-report`, `request-queue`).
6. Chat **`!talents`** (Ctrl+1) and **`!stats`** (Ctrl+2) show those Ahli panels for 10 seconds (2 minute cooldown each). Talents still open automatically at talent times.

## Maps and modes

Heroes Profile loop is **Storm League** on the current patch. Channel-point rewards can also queue **Quick Match** and **ARAM**. Unranked Draft and brawls are not supported (Unranked Draft is gone from current clients).

**Ranked / QM (15):** Infernal Shrines, Sky Temple, Cursed Hollow, Dragon Shire, Towers of Doom, Tomb of the Spider Queen, Volskaya Foundry, Garden of Terror, Blackheart's Bay, Warhead Junction, Alterac Pass, Battlefield of Eternity, Hanamura Temple, Haunted Mines, Braxis Holdout.

**ARAM (reward / ReplayId only):** Silver City, Lost Cavern, Industrial District, Braxis Outpost.

ReplayId rewards should use a **recent current-patch** id. Older `.StormReplay` files need a matching `Versions\Base*` client already on disk; Blizzard often stops serving old builds.

Catalog: `Maps:Catalog` in `appsettings.json` (`Playable`, `RankedRotation`, `Type`).

## Requirements

- Windows 10/11 (WinRT OCR, **PrintWindow** capture, process control). The client must be **windowed 1080p** (`heroesreplay client configure`). Fullscreen D3D11 is not supported.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Heroes of the Storm + Battle.net
- Optional: OBS Studio 30.0+ (obs-websocket 5.3+, for `SetRecordDirectory`) with **Tools → WebSocket Server Settings** enabled (port **4455**). `heroesreplay obs validate` checks the running OBS offers every request in `ObsValidator.RequiredRequests`
- Optional: Twitch app credentials, Heroes Profile API key (Bearer).

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

## Observability (Aspire Dashboard)

Traces, metrics, and logs go to OTLP `http://127.0.0.1:4317` (override with `OTEL_EXPORTER_OTLP_ENDPOINT`). The standalone Aspire dashboard is the local `Aspire.Cli` dotnet tool (`aspire dashboard run`). No Docker and no AppHost.

Aspire CLI 13.5.4 also starts an MCP server: `aspire agent mcp --dashboard-url http://127.0.0.1:18888`. Dashboard-only mode (no AppHost) exposes `list_structured_logs`, `list_traces`, and `list_trace_structured_logs`. Resource start/stop tools are not available. `aspire agent init` writes that server into `.mcp.json` for detected agents. `aspire mcp tools` / `aspire mcp call` talk to MCP tools on Aspire resources; they are not this observability server.

```powershell
dotnet tool restore
heroesreplay otel up
# UI: http://127.0.0.1:18888
# OTLP gRPC: http://127.0.0.1:4317
heroesreplay check heroesprofile
heroesreplay spectate file --file C:\heroesreplay\Replays\65277396.StormReplay
heroesreplay otel down
```

`spectate` and `services start` start that dashboard when port 4317 is not already listening. If the Aspire CLI is missing or the dashboard does not come up, spectating continues and the failure is logged. Set `OpenTelemetry:Enabled` to `false` to disable export.

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
| `services start` / `services stop` / `services status` | Run spectate, Twitch, the Heroes Profile downloader, and the YouTube uploader as separate processes |
| `spectate heroesprofile` | Spectate `.StormReplay` files already in `Data\Standard` and `Data\Requests` |
| `heroesprofile download` | List and download Storm League replays into those folders |
| `heroesprofile patch-index` | Find the first replay id of the current patch line (`--write` sets `MinReplayId`) |
| `calculators coordinates` | Parse the newest Documents replay and prove coordinates + focus timeline |
| `calculators report --file <path>` | Write a spectator report |
| `calculators units --directory <path>` | Sample 1–5 replays per map, one file at a time, and write unit CSVs |
| `twitch connect` / `twitch rewards …` | Chat bot and channel-point rewards |
| `youtube uploader` | Upload OBS recordings |
| `youtube library` | File uploaded matches into map, mode, rank, unusual-draft, viewer-review, and patch playlists (`YouTube:Playlists`) |
| `client configure` | Windowed 1080p + AhliObs in `Documents\Heroes of the Storm` (Variables.txt + StormInterface). Quit the game first. |
| `client status` / `check client` | Verify that preset |
| `check` | Config + Heroes Profile + OBS + Twitch + client + Battle.net + connectivity (continues on failure) |
| `check config` / `check heroesprofile` / `check obs` / `check twitch` / `check client` / `check battlenet` / `check connectivity` | One integration at a time |
| `check timer` / `check twitch-extension` | Read-only memory scan for the match clock; Heroes Profile Twitch extension key |
| `otel up` / `otel down` / `otel status` | Standalone Aspire dashboard |
| `obs arm` / `obs disarm` / `obs status` | Machine-local Twitch ingest arm. Ingest needs it and `OBS:StreamingEnabled` |
| `obs pages [--no-reload]` | Render `Data\queue.html` and `Data\prediction-report.html` with this build, then reload the OBS browser sources that show them. Run it after a build or an update so OBS shows the new page layout |
| `obs inspect` / `obs validate [--output text|json]` | Read live OBS without changing it (versions, profile and collection, canvas and FPS, recording format, scenes, audio, stream service without the key), or validate it against `obs/Default.json` and this install's settings with stable codes. `validate` exits 1 on an error finding |
| `update check` | Compare this install with the latest GitHub Release |
| `mcp` | Stdio MCP server for agents (`get_spectator_status`, `get_current_focus`, checks, and read-only OBS tools `obs_inspect`, `obs_validate`, `obs_screenshot`). Pair with a running `spectate` process. No MCP tool changes OBS or exposes the stream key. |

No command needs an elevated (administrator) shell except `client firewall`, which adds the Windows Firewall rule for each installed Heroes client. `spectate` and the services run unelevated. Invalid input, such as `spectate file --player 11`, exits 1 before anything runs. `services stop` exits 1 unless every role exited, Heroes of the Storm closed, and OBS is not streaming; it prints each role as graceful, killed, already exited, or still running.

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
| `src/HeroesReplay.Core` | Feature slices: analysis, spectating, game client, OBS, Twitch, YouTube, service host |
| `src/HeroesReplay.HeroesProfile.Client` | Kiota client for the Heroes Profile v1 API ([docs/heroesprofile-api.md](docs/heroesprofile-api.md)) |
| `src/HeroesReplay.Tests` | xUnit |

The solution file is **`heroes-replay.slnx`** (XML). Do not add a parallel `.sln`.

## Agent instructions

See [AGENTS.md](AGENTS.md). Repo skills live in [`.agents/skills/`](.agents/skills/).

## License

MIT. Copyright (c) Patrick Magee.
