# HeroesReplay production runbook

This file ships inside the release zip. It is for a coding agent (Claude Code, Grok Build, or similar) started in the install folder `C:\heroesreplay\app` on the production machine. Nothing here needs the git repository. Do not clone it or run `dotnet build` on this machine.

HeroesReplay is a 24/7 automated spectator for Heroes of the Storm. It downloads Storm League replays, plays them in the game client, drives the camera, records and streams through OBS to Twitch (`saltysadism`), runs Twitch predictions and channel-point requests, and uploads recordings to YouTube.

Your job here is to keep that stack running: preflight, start, watch, stop, restart, and update. You do not change code.

## 0. Check the machine first

```powershell
hostname
```

| Hostname | What to do |
| --- | --- |
| `DESKTOP-8SJE72` | Production. Follow this file. |
| `ASA-SERVER` | Development. Stop. Use the repo `AGENTS.md` in `C:\heroesreplay\HeroesReplay` instead. Never set `HEROES_REPLAY_ENV=prod` and never go live there. |
| Anything else | Ask the user before running anything. A new production machine needs the first-install steps (section 7). |

## 1. Rules

- **This is the live stream.** Viewers are watching. Do not stop the stack, close Heroes of the Storm, or close OBS in the middle of a match unless the user asks for it, or the stack is already broken (section 5). Prefer to act between replays: `status.json` phase is not `TimerDetected`.
- **Every command runs from `C:\heroesreplay\app` with `HEROES_REPLAY_ENV=prod`.** Without it the CLI loads dev settings: dry-run YouTube, no streaming, no self-update.
- **Never print, log, paste, or commit a secret value.** `check config` and `ensure-secrets.ps1` print key names and lengths only. Do not `Get-Content` the secrets files into the conversation.
- **Use the credentials already on the machine.** They are in `C:\heroesreplay\secrets` and in the 1Password service account (`OP_SERVICE_ACCOUNT`). Do not ask the user to type tokens into the chat. If they are missing or invalid, say which key is missing and stop.
- **Do not click in Battle.net or Heroes of the Storm.** No Play, no Update, no Allow, no typing a password into the game's login form. The spectator owns the client.
- **Do not edit `appsettings.json` or `appsettings.prod.json`.** A release replaces them. A per-machine override belongs in an environment variable (`HEROES_REPLAY_<Section>__<Key>`), and only when the user asks.
- **Do not copy OBS `service.json`.** It holds the stream key and is set up in OBS by hand.

## 2. Layout

| Path | What |
| --- | --- |
| `C:\heroesreplay\app` | This install. `heroesreplay.exe`, `appsettings*.json`, `version.txt`, `apply-release.ps1`, `ensure-secrets.ps1`, `fill-secrets-from-op.ps1`, `obs\`. A release replaces this folder. |
| `C:\heroesreplay\app\appsettings.secrets.json` | Live secrets the CLI reads. Not in the zip. |
| `C:\heroesreplay\app.previous` | The install before the last update, kept until the new one has been healthy. |
| `C:\heroesreplay\secrets\appsettings.secrets.json` | Backup the updater copies back after every update. |
| `C:\heroesreplay\secrets\client_secrets.json` | Backup of the YouTube OAuth client. |
| `C:\heroesreplay\Data` | Replay queue and state: `Standard\`, `Requests\`, `Contexts\<replayId>\`, `requests.json`, `spectated-ids.txt`, `client_secrets.json`. Never delete it. An update never touches it. |
| `C:\heroesreplay\Battle.net\Battle.net.exe` | Battle.net. Must stay signed in. |
| `C:\Program Files (x86)\Heroes of the Storm` | Game install. |
| `%LOCALAPPDATA%\HeroesReplay\status.json` | Spectator heartbeat, written every second. |
| `%LOCALAPPDATA%\HeroesReplay\services.json` | Pids that `services start` launched. |
| `%LOCALAPPDATA%\HeroesReplay\logs\` | Pid files and Aspire dashboard logs. |
| `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` | OBS scene collection. |

## 3. Preflight

Run these before any start. Every step is read-only except `ensure-secrets.ps1`, which only copies secrets files into place.

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
Get-Content .\version.txt
powershell -NoProfile -ExecutionPolicy Bypass -File .\ensure-secrets.ps1
.\heroesreplay.exe check config
.\heroesreplay.exe check
.\heroesreplay.exe update check
```

`ensure-secrets.ps1` makes sure `appsettings.secrets.json` is beside the exe. It uses, in order: the file already in the install, the `C:\heroesreplay\secrets` backup, then 1Password through `fill-secrets-from-op.ps1` (needs the `op` CLI and user env `OP_SERVICE_ACCOUNT`). It then backs up a good file, restores `Data\client_secrets.json`, and exits 1 with the missing key names if nothing worked. Exit 1 means stop and tell the user which keys are missing.

`check` tests config, Heroes Profile, OBS, Twitch, and the client preset. It exits 1 when any fail, but it keeps going so you see all of them. How to read a failure:

| Failure | Meaning | Action |
| --- | --- | --- |
| `config` / a secret missing | Secrets file incomplete | `ensure-secrets.ps1`, then `check config` again |
| `heroesprofile` 401/403 | Heroes Profile API key invalid | Tell the user. Refill from 1Password with `fill-secrets-from-op.ps1` if they rotated it there. |
| `twitch` 401 | Twitch token expired or revoked | Same. The refresh token renews access while it is valid. A revoked refresh token needs a new token in 1Password. |
| `obs` cannot connect | OBS is closed or WebSocket is off | Not a blocker. The spectator launches OBS. If OBS is open, check Tools → WebSocket Server, port 4455. |
| `client` mismatch | Variables.txt or AhliObs not set | Only while Heroes is closed: `.\heroesreplay.exe client configure` |

## 4. Start, status, stop

### Start

Use the same order the updater uses, so the machine's own launcher wins when it has one:

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
schtasks /Query /TN HeroesReplay-live *> $null
if ($LASTEXITCODE -eq 0) { schtasks /Run /TN HeroesReplay-live }
elseif (Test-Path "$env:LOCALAPPDATA\HeroesReplay\start-live.cmd") { Start-Process "$env:LOCALAPPDATA\HeroesReplay\start-live.cmd" -WorkingDirectory C:\heroesreplay\app }
else { .\heroesreplay.exe services start }
```

`services start` launches four separate processes, each in its own console window, and records them in `services.json`:

| Process | Command | Does |
| --- | --- | --- |
| Spectator | `spectate heroesprofile` | Plays downloaded replays in the game, OCR clock, camera, OBS scenes, recording, stream reconcile, self-update between replays. Writes `status.json`. |
| Twitch | `twitch connect` | Chat, channel-point replay requests, Blue/Red predictions. |
| Downloader | `heroesprofile download` | Lists and downloads Storm League replays into `Data\Standard`, and requested ones into `Data\Requests`. |
| YouTube | `youtube uploader` | Uploads finished recordings from `Data\Contexts`. |

It also starts the Aspire dashboard (`http://127.0.0.1:18888`) when the `aspire` tool is installed. A dashboard failure does not stop the services.

Do not start any of the four by hand as well. Two spectators fight over the game window.

### Status

```powershell
.\heroesreplay.exe services status
Get-Content "$env:LOCALAPPDATA\HeroesReplay\status.json"
```

Healthy:

- All four processes alive.
- `status.json` `updatedAt` within the last 15 seconds.
- While a match plays: phase `TimerDetected`, the timer advancing, a focused hero.
- Between matches: the waiting scene, then the next replay loading within a few minutes. `OBS:BeforeNextReplay` holds for 90 seconds after each game.

Logs and traces: the Aspire dashboard at `http://127.0.0.1:18888`, or `aspire otel logs`. Each process has its own service name. Look for unhandled exception stacks, and repeated invalid-timer messages.

### Stop

Only when the user asks, before an update, or when the stack is broken.

```powershell
.\heroesreplay.exe services stop
```

This writes `services.stop`, waits up to 20 seconds, kills any recorded pid still alive, and closes Heroes of the Storm. Afterwards confirm with `services status` and `Get-Process HeroesOfTheStorm_x64 -ErrorAction SilentlyContinue`. An open game with no spectator is a stuck stream.

### Restart

`services stop`, confirm everything is gone, run the preflight, then start.

## 5. When something is wrong

| Symptom | What to do |
| --- | --- |
| `status.json` stale for more than a minute, spectator pid gone | Restart (section 4). |
| Spectator alive but the same phase for more than 10 minutes with no timer | Look at the dashboard logs for that replay first, so you can report what stalled. Then restart. |
| Game shows a Battle.net login form | Battle.net signed out. Do not type credentials. Tell the user to sign in to Battle.net on this machine. |
| Battle.net shows Update for Heroes | A new patch. Do not click it. Tell the user. The spectator plays retained older builds meanwhile. |
| Stream offline but OBS open | The spectator reconciles stream start and stop. If it stays offline after a full replay, check `check obs` and the OBS stream settings, then tell the user. Do not paste the stream key anywhere. |
| No new replays | `heroesprofile download` alive? `check heroesprofile` passes? Files appearing in `Data\Standard`? |
| YouTube uploads failing | `Data\client_secrets.json` present? Quota errors clear at midnight Pacific; leave them. |
| `heroesreplay.exe` will not start, missing DLL or runtime | The .NET 10 Desktop Runtime is missing. Tell the user. Do not install software without asking. |

If a fix needs more than a restart, stop and report what you saw: the failing command, the exit code, and the log lines. Leave the stack running if it is still spectating.

## 6. Updates

Updates are automatic. With `HEROES_REPLAY_ENV=prod`, the spectator checks GitHub after each replay. When `HeroesReplay/HeroesReplay` has a newer release than `version.txt`, it downloads `heroesreplay-win-x64.zip`, stops the services, and runs `apply-release.ps1`. That script waits for every `heroesreplay` process to exit, keeps `app.previous`, copies the new files in, copies the secrets back from `C:\heroesreplay\secrets`, copies OBS scenes only when OBS is closed, and starts the stack again. A new release also replaces this file.

`.\heroesreplay.exe update check` prints the installed and latest version without changing anything.

Manual update, only when the user asks or the automatic one is stuck:

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
$stage = Join-Path $env:TEMP 'heroesreplay-release'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Invoke-WebRequest https://github.com/HeroesReplay/HeroesReplay/releases/latest/download/heroesreplay-win-x64.zip -OutFile "$stage.zip"
Expand-Archive "$stage.zip" -DestinationPath $stage -Force
.\heroesreplay.exe services stop
powershell -NoProfile -ExecutionPolicy Bypass -File "$stage\apply-release.ps1" -InstallDir C:\heroesreplay\app -StagingDir $stage
```

Run the staged copy of `apply-release.ps1`, not the one in the install, because it replaces the install folder. Then check `version.txt` and `services status`.

Roll back: `services stop`, copy `C:\heroesreplay\app.previous\*` over `C:\heroesreplay\app`, then run the preflight and start. Tell the user which version you rolled back from.

## 7. First install on a new production machine

Ask the user before doing this. It assumes Windows 11, Heroes of the Storm installed at the default path, Battle.net at `C:\heroesreplay\Battle.net` and signed in, OBS Studio installed with WebSocket enabled on port 4455, and the .NET 10 Desktop Runtime.

1. Create `C:\heroesreplay\Data\{Standard,Requests,Contexts,HeroesData}`, `C:\heroesreplay\Replays`, and `C:\heroesreplay\secrets`.
2. Download the latest `heroesreplay-win-x64.zip` and extract it to `C:\heroesreplay\app`.
3. Secrets: put `appsettings.secrets.json` and `client_secrets.json` in `C:\heroesreplay\secrets`, or set user env `OP_SERVICE_ACCOUNT` and install the 1Password CLI. Then `ensure-secrets.ps1`.
4. With Heroes closed: `.\heroesreplay.exe client configure`.
5. With OBS closed, copy `obs\Default.json` to `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` and `obs\Default\basic.ini` to `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\basic.ini`. The stream key is set in OBS by the user.
6. Preflight (section 3), then start (section 4).

## 8. MCP servers in this folder

`.mcp.json` (Claude Code) and `.grok/config.toml` (Grok) register two servers when the agent starts in `C:\heroesreplay\app`:

| Server | Tools |
| --- | --- |
| `heroesreplay` (`heroesreplay.exe mcp`, prod) | `get_spectator_status`, `get_current_focus`, `check_config`, `check_heroesprofile`, `check_obs`, `check_twitch`, `check_battlenet`. A separate process from the spectator; it reads `status.json`. |
| `aspire` (`aspire agent mcp --dashboard-url http://127.0.0.1:18888`) | `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. Needs the `aspire` dotnet tool on PATH and the dashboard running. |

If a server does not connect, the CLI commands above give the same answers.

## Command reference

All from `C:\heroesreplay\app` with `HEROES_REPLAY_ENV=prod`. `.\heroesreplay.exe <command> --help` shows options.

| Command | Use |
| --- | --- |
| `services start` / `status` / `stop` | Run the stack. |
| `check` | All connectivity checks. Subcommands: `config`, `heroesprofile`, `obs`, `twitch`, `client`, `connectivity`, `timer`, `battlenet`. |
| `update check` | Installed vs latest release. |
| `client configure` / `client status` | Windowed 1080p and AhliObs. Configure only while Heroes is closed. |
| `otel up` / `otel status` / `otel down` | Aspire dashboard. |
| `twitch rewards list` | Read the channel-point rewards. Do not `submit` or `remove` unless asked. |
| `youtube library --once` | File uploaded videos into playlists once. Not part of `services start`. |
| `mcp` | Stdio MCP server for agents. |

Do not run `spectate file`, `twitch predictions test`, `twitch rewards test`, or `calculators` here. Those are development tools and some of them post to the live channel.
