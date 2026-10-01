# HeroesReplay production runbook

This file ships inside the release zip. It is for a coding agent (Claude Code, Grok Build, or similar) started in the install folder `C:\heroesreplay\app` on the production machine. Nothing here needs the git repository. Do not clone it or run `dotnet build` on this machine.

HeroesReplay is a 24/7 automated spectator for Heroes of the Storm. It downloads Storm League replays from Heroes Profile, plays them in the game client, drives the camera, records and streams through OBS to Twitch (`twitch.tv/saltysadism`), runs Twitch predictions and channel-point replay requests, and uploads recordings to YouTube.

Your job is to keep that stack running: install, preflight, start, watch, stop, restart, update. You do not change code.

## 0. What the user probably means

| The user says | Do |
| --- | --- |
| "run heroesreplay", "start the stream", "bring it up" | Section 0 check, then **section 5 (preflight)**, **section 6 (start)**, **section 7 (verify the stream)**. If `C:\heroesreplay\app\heroesreplay.exe` does not exist, **section 12 (first install)** first. |
| "get the latest release", "update" | `update check` (section 10). If newer: **section 10 manual update**, then section 7. |
| "is it working?", "check on it" | **Section 8 (health)** and **section 9 (telemetry)**. Change nothing. |
| "restart it" | **Section 6 restart**. |
| "stop it" | **Section 6 stop**. This also takes the Twitch stream offline. |

Always start here:

```powershell
hostname
whoami /groups | Select-String 'S-1-16-12288'   # present = elevated (High Mandatory Level)
```

| Hostname | What to do |
| --- | --- |
| `DESKTOP-8SJE72` | Production. Follow this file. Only this hostname may stream: the app refuses Twitch ingest on any other host. |
| `ASA-SERVER` | Development. Stop. Use the repo `AGENTS.md` in `C:\heroesreplay\HeroesReplay`. Never set `HEROES_REPLAY_ENV=prod` and never go live there. |
| Anything else | Ask the user before running anything. |

If the shell is not elevated, `services start` and every `spectate` command fail ("You must be running this application as an administrator", or `spectate failed: privilege check failed.`). Ask the user to start the agent from an elevated terminal, or use the scheduled task (section 11), which runs elevated.

## 1. Rules

- **This is the live stream.** Viewers are watching. Do not stop the stack, close Heroes of the Storm, or close OBS while a match is playing (`status.json` phase `TimerDetected`) unless the user asks or the stack is already broken.
- **Every command runs from `C:\heroesreplay\app` with `$env:HEROES_REPLAY_ENV = 'prod'`, in an elevated PowerShell.** Without the variable the CLI loads only the base settings: no streaming, YouTube dry-run, no recording, no self-update. Child processes inherit the variable from the shell that runs `services start`.
- **Never print, log, paste, or commit a secret value.** Not the secrets files, not tokens, not the OBS stream key. `check config` and `ensure-secrets.ps1` print names, lengths, and scopes only. Read a secret into a variable if a command needs it; never echo it.
- **Use the credentials already on the machine**: `C:\heroesreplay\secrets` and the 1Password service account (`OP_SERVICE_ACCOUNT`). Do not ask the user to type tokens into chat. If one is missing or rejected, name the key and stop.
- **Do not click in Battle.net or Heroes of the Storm.** No Play, Update, Allow, or typing a password. The spectator owns the client.
- **Do not edit `appsettings*.json` in the install.** A release replaces them. A per-machine override is an environment variable `HEROES_REPLAY_<Section>__<Key>`, only when the user asks.
- **Never run the dev commands in section 13**, and never run a second spectator next to `services start`.

## 2. Layout

| Path | What |
| --- | --- |
| `C:\heroesreplay\app` | This install: `heroesreplay.exe`, `appsettings*.json`, `version.txt`, `obs\`, `Assets\Interfaces\AhliObs 0.75.StormInterface`, `apply-release.ps1`, `ensure-secrets.ps1`, `fill-secrets-from-op.ps1`, `register-autostart.ps1`, this file. Replaced by every release. |
| `C:\heroesreplay\app\appsettings.secrets.json` | Live secrets. Not in the zip. Restored from the backup on every update. |
| `C:\heroesreplay\app\role-ready.txt` | Written by `services start` when all four roles are ready. The updater needs it to discard `app.previous`. |
| `C:\heroesreplay\app.previous` | The install before the last update. Rollback source. |
| `C:\heroesreplay\secrets\` | `appsettings.secrets.json` and `client_secrets.json` backups. The updater installs this copy, so keep it current. |
| `C:\heroesreplay\Data\` | All state. Never delete it. `Standard\` and `Requests\` (replay cache), `Contexts\<replayId>\` (recordings, `end.png`), `HeroesData\` (heroes-data2), `Clients\` (kept old game builds), `requests.json`, `spectated-ids.txt`, `client_secrets.json`. |
| `C:\heroesreplay\Battle.net\Battle.net.exe` | Battle.net. Must stay signed in. |
| `C:\Program Files (x86)\Heroes of the Storm` | Game. `Versions\Base*\HeroesOfTheStorm_x64.exe` are the installed builds. |
| `C:\Program Files\obs-studio\bin\64bit\obs64.exe` | OBS (override: `HEROES_REPLAY_OBS__ExecutablePath`). |
| `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` | OBS scene collection. |
| `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\` | OBS profile: `basic.ini` (video/encoder) and `service.json` (Twitch stream key, set by hand). |
| `%LOCALAPPDATA%\HeroesReplay\` | `status.json` (spectator heartbeat), `services.json` (running pids), `services.stop`, `ready\`, `updates\`, `logs\` (pid files, `aspire-dashboard*.log`, `apply-release.log`). |

## 3. How it runs

`heroesreplay.exe services start` checks prerequisites, then launches four separate processes, each in its own console window, waits for each to report ready, records them in `services.json`, and exits. Nothing restarts a role that dies later; you do (section 8).

| Role | Command (started for you) | Does | Telemetry name |
| --- | --- | --- | --- |
| spectate | `spectate heroesprofile` | Plays cached replays in the game, reads the HUD clock, picks heroes, drives OBS scenes and recording, **launches OBS and keeps the Twitch stream live**, retention cleanup, **self-update after each replay**. Writes `status.json`. | `heroesreplay-spectate` |
| twitch | `twitch connect` | Chat, channel-point replay requests (EventSub), Blue/Red predictions from `status.json`. Syncs the reward list at start. | `heroesreplay-twitch` |
| download | `heroesprofile download` | Lists and downloads Storm League replays into `Data\Standard`, requested ones into `Data\Requests`. | `heroesreplay-download` |
| youtube | `youtube uploader` | Uploads finished recordings from `Data\Contexts` and files them into playlists. | `heroesreplay-youtube` |

`services start` also launches the Aspire dashboard (section 9) when it is not running, and opens it in a browser.

The spectator, not `services start`, launches OBS (`--profile "HeroesReplay" --collection "HeroesReplay"`) and starts streaming within seconds of starting, then re-checks every 15 seconds and restarts a stopped stream. A graceful `services stop` stops the stream. An OBS that was already open is used as it is, with whatever profile it has loaded.

## 4. Prerequisites

Check these on a new machine, after a Windows or game update, and whenever start fails. Read-only probe:

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
dotnet --list-runtimes | Select-String 'Microsoft.WindowsDesktop.App 10\.'
powershell -NoProfile -Command "[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]::AvailableRecognizerLanguages | % LanguageTag"
Test-Path 'C:\Program Files (x86)\Heroes of the Storm'
Get-ChildItem 'C:\Program Files (x86)\Heroes of the Storm\Versions\Base*\HeroesOfTheStorm_x64.exe' | % { "$($_.Directory.Name) $($_.VersionInfo.FileVersion)" }
Test-Path C:\heroesreplay\Battle.net\Battle.net.exe
Test-Path 'C:\Program Files\obs-studio\bin\64bit\obs64.exe'
Test-Path "$env:APPDATA\obs-studio\basic\profiles\HeroesReplay\basic.ini"
Test-Path "$env:APPDATA\obs-studio\basic\profiles\HeroesReplay\service.json"
Get-Content "$env:APPDATA\obs-studio\plugin_config\obs-websocket\config.json" -ErrorAction SilentlyContinue | ConvertFrom-Json | Select-Object server_enabled, server_port, auth_required
Get-Command aspire -ErrorAction SilentlyContinue | % Source
Get-ChildItem "$env:APPDATA\Google.Apis.Auth" -ErrorAction SilentlyContinue | % Name
.\heroesreplay.exe data status
.\heroesreplay.exe client status
```

| Prerequisite | Expected | If missing |
| --- | --- | --- |
| Interactive desktop session | The streaming user is logged on (not just a service). The game, OBS game capture, OCR, and medium-integrity launches need the desktop and Explorer. | Ask the user to log on. Auto-logon plus section 11 survives reboots. |
| Elevated shell | High Mandatory Level | Section 0. |
| .NET 10 Desktop Runtime x64 | `Microsoft.WindowsDesktop.App 10.x` | Ask before installing: `winget install Microsoft.DotNet.DesktopRuntime.10`. |
| Windows OCR language | Includes `en-US` | Ask, then elevated: `Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"`. Without it `spectate failed: OCR engine was not created.` |
| Heroes of the Storm | Install folder and at least one `Base*` exe | Tell the user. Never click Update. |
| Battle.net | Exe at that path, running, signed in | Tell the user to sign in. Never type credentials. |
| OBS Studio 28+ | `obs64.exe` at that path | Ask before installing: `winget install OBSProject.OBSStudio`. |
| OBS WebSocket | `server_enabled` true, port 4455, `auth_required` false (or the password in `HEROES_REPLAY_OBS__WebSocketPassword`) | In OBS: Tools > WebSocket Server Settings. Ask the user; it is a UI setting. |
| OBS Twitch stream key | `profiles\HeroesReplay\service.json` exists | The user sets it in OBS: profile HeroesReplay, Settings > Stream > Twitch (key in 1Password `op://Heroes Replay/Twitch SaltySadism/stream key`). Never write or print the key yourself. No key means no stream. |
| OBS profile | `profiles\HeroesReplay\basic.ini` | Section 12 step 5. 1080p60, x264 6000 kbps stream, Quick Sync (Intel GPU) recording into `Data\Contexts`. |
| Aspire CLI | `aspire` on PATH | `dotnet tool install -g aspire.cli --version 13.5.4`, then a new shell. Without it nothing fails, but there is no dashboard (section 9). |
| heroes-data2 | `data status` prints `Hero data: herodata_<build>.json`, exit 0 | `heroesreplay data download`. After a game patch: `heroesreplay data download --force`, then restart. `services start` also downloads it when the cache is empty. |
| Client preset | `client status` exit 0 (windowed 1080p, AhliObs) | Only while Heroes is closed: `heroesreplay client configure`. The spectator also does this itself before launching a new client. |
| YouTube OAuth token | A `Google.Apis.Auth...TokenResponse-UCpf5rn5UlJTUZF9n98HXS5A` file | The first upload opens a browser for Google consent. The user must complete it at the desktop. Uploads wait until then; other roles run. |
| Secrets | `ensure-secrets.ps1` exit 0 | Section 5. |
| 1Password CLI `op` | Only needed to refill secrets | Ask before installing: `winget install AgileBits.1Password.CLI`. |
| ffmpeg | `C:\ffmpeg\bin\ffmpeg.exe` (optional) | Without it team-kill clips are skipped with a warning. |
| Display | 1920x1080 or larger, 100% scaling assumed | Tell the user. |

Firewall rules for each game build, retained old game builds in `Data\Clients`, AhliObs, and `Variables.txt` are handled by the spectator itself.

## 5. Preflight

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
Get-Content .\version.txt
powershell -NoProfile -ExecutionPolicy Bypass -File .\ensure-secrets.ps1
.\heroesreplay.exe data status
.\heroesreplay.exe check config
.\heroesreplay.exe check
.\heroesreplay.exe update check
```

**`ensure-secrets.ps1`** puts `appsettings.secrets.json` beside the exe. It takes the install's own file, else the `C:\heroesreplay\secrets` backup, else 1Password (needs `op` and user env `OP_SERVICE_ACCOUNT`). Then it:

- validates the Twitch token with Twitch and writes `Twitch:GrantedScopes` from it (`services start` refuses the Twitch role without chat, redemption, and prediction scopes);
- keeps the newer of the install file and the backup in both places, because every update installs the backup;
- restores `Data\client_secrets.json` (YouTube OAuth client), which the YouTube role requires.

Exit 1 means stop and report the lines it printed (key names only). `-Offline` skips the Twitch call.

**`check`** runs config, heroesprofile, obs, twitch, client, battlenet, and connectivity. Output is `[OK|FAIL] name: detail`; exit 1 if any fail.

| Failure | Meaning | Action |
| --- | --- | --- |
| `config` | Heroes Profile key missing | `ensure-secrets.ps1` |
| `heroesprofile` | Key rejected or API down | Tell the user. A rotated key: refill from 1Password with `fill-secrets-from-op.ps1`, then `ensure-secrets.ps1`. |
| `twitch` (`validate-failed-401`) | Token expired or revoked. The app does not refresh Twitch tokens. | Tell the user: a new token goes into 1Password, then `fill-secrets-from-op.ps1` and `ensure-secrets.ps1`. |
| `obs` "No Identify" | OBS closed, or WebSocket off | Not a blocker if OBS is closed: the spectator launches it. If open, check the WebSocket settings (section 4). |
| `obs` scene or asset errors | The install's `obs\` bundle is incomplete | Reinstall the release (section 10). |
| `client` | Variables.txt or AhliObs not set | Heroes closed: `client configure` |
| `battlenet` | Battle.net window not visible, or shows Update | Tell the user. Do not click. |
| `connectivity` | No internet | Wait. The stream is not stopped by an outage; OBS reconnects. |

## 6. Start, stop, restart

### Start

Never start while `services status` shows running processes, or while any other `heroesreplay.exe spectate` runs:

```powershell
.\heroesreplay.exe services status
Get-CimInstance Win32_Process -Filter "Name='heroesreplay.exe'" | Select-Object ProcessId, CommandLine
```

A leftover `services.stop` is cleared by `services start`. Start in the same order the updater uses:

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
schtasks /Query /TN HeroesReplay-live *> $null
if ($LASTEXITCODE -eq 0) { schtasks /Run /TN HeroesReplay-live }
elseif (Test-Path "$env:LOCALAPPDATA\HeroesReplay\start-live.cmd") { Start-Process "$env:LOCALAPPDATA\HeroesReplay\start-live.cmd" -WorkingDirectory C:\heroesreplay\app }
else { .\heroesreplay.exe services start }
```

The task and `start-live.cmd` set `HEROES_REPLAY_ENV=prod` themselves and run elevated. Direct `services start` prints four `Started <role> pid N` lines and exits 0. Then wait ~1 minute and run `services status`: `Services: 4 of 4 still running.`

If start fails it stops what it started and exits 1 with `<role> failed: <reason>`:

| Reason | Fix |
| --- | --- |
| `spectate failed: privilege check failed.` | Not elevated (section 0). |
| `spectate failed: OCR engine was not created.` | OCR language (section 4). |
| `spectate failed: game or data paths are not available.` | Heroes install, `C:\heroesreplay\Data`, or Battle.net exe missing. |
| `spectate failed: OBS prerequisites failed.` | `obs64.exe` not found. |
| `twitch failed: Twitch token or client id is missing.` | `ensure-secrets.ps1` |
| `twitch failed: Twitch scopes are not valid.` | `ensure-secrets.ps1` (fills `GrantedScopes`). If it says the token lacks a scope, the token must be reissued. |
| `download failed: Heroes Profile game data is not ready.` | `heroesreplay data download` |
| `download failed: Heroes Profile credential is missing.` | `ensure-secrets.ps1` |
| `youtube failed: OAuth client secrets are missing.` | `ensure-secrets.ps1` restores `Data\client_secrets.json`. |
| `<role> failed: exited before ready.` / `ready timed out.` | Read that role's console window and section 9 logs. |
| `Service startup failed: role configuration could not be loaded.` | A bad setting, or an `op://` value without `op`. Run `check config`. |
| `Services already running.` | Already up. Use `services status`. |

### Stop

Only when the user asks, before a manual update, or when the stack is broken. Prefer between matches.

```powershell
.\heroesreplay.exe services stop
.\heroesreplay.exe services status
Get-Process HeroesOfTheStorm_x64, heroesreplay -ErrorAction SilentlyContinue
```

`services stop` signals every role, waits up to 20 seconds, kills what is left, and closes Heroes of the Storm. The spectator stops the Twitch stream as it shuts down. OBS stays open. After a forced kill the stream may still be live: tell the user, and stop it in OBS only if they ask. Run from an elevated shell, or the kill of elevated roles fails.

### Restart

Wait for a non-`TimerDetected` phase, `services stop`, confirm nothing is left, preflight (section 5), start.

### One role died

`services status` shows `3 of 4` and exits 1. `services start` refuses while the others live. Restart the whole stack.

## 7. OBS and the Twitch stream

What the app does on `DESKTOP-8SJE72` with prod settings: the spectator launches OBS if it is closed, connects to the WebSocket, shows `waiting-screen`, calls StartStream (4 tries), and every 15 seconds starts the stream again if it stopped. A network outage does not stop the stream; OBS reconnects on its own (25 retries).

What it never does: set the stream destination or key, enable the WebSocket server, or adopt an OBS that was already open with a different profile.

Verify after a start, and whenever asked about the stream:

```powershell
.\heroesreplay.exe services status            # obs process=True websocket=True scene=... stream desired=True active=True
.\heroesreplay.exe check obs
Get-Process obs64 | Select-Object Id, StartTime, MainWindowTitle   # title shows the profile and collection
```

The OBS fields in `status.json` and `services status` update only on startup and connectivity changes, so they can be stale. The ground truth is Twitch. Without printing the token:

```powershell
$s = Get-Content C:\heroesreplay\app\appsettings.secrets.json -Raw | ConvertFrom-Json
$live = Invoke-RestMethod 'https://api.twitch.tv/helix/streams?user_login=saltysadism' -Headers @{ 'Client-Id' = $s.Twitch.ClientId; Authorization = "Bearer $($s.Twitch.AccessToken)" }
if ($live.data.Count) { "LIVE since $($live.data[0].started_at), $($live.data[0].viewer_count) viewers" } else { 'OFFLINE' }
Remove-Variable s, live
```

Telemetry: `aspire otel logs heroesreplay-spectate --dashboard-url http://127.0.0.1:18888 --search "OBS stream" -n 20`. "OBS stream is active" is good. "OBS stream ... was not confirmed" means it failed.

If the stream stays offline for more than a minute while the spectator runs:

1. `profiles\HeroesReplay\service.json` missing: the key was never set. The user must set it in OBS.
2. `check obs` cannot identify: WebSocket disabled or password mismatch.
3. OBS was already open with another profile or collection: tell the user. With their OK, close OBS between matches; the spectator relaunches it with the right profile within 15 seconds.
4. Twitch rejects the key (OBS shows an error): the user resets the key.

## 8. Health

```powershell
.\heroesreplay.exe services status
Get-Content "$env:LOCALAPPDATA\HeroesReplay\status.json" | ConvertFrom-Json | Select-Object updatedAt, phase, timer, map, replayId, outcome, completedReplayId, completedAt, obsStreamActive
Get-Process HeroesOfTheStorm_x64, obs64 -ErrorAction SilentlyContinue | Select-Object Name, Id, Responding
```

`status.json` phases: `Loading` (launch, before the clock), `TimerDetected` (match playing; predictions open), `EndDetected` (match over; see `outcome`), `Waiting` (a client dialog; retried in 2 minutes), `Idle` (no replay to play).

A healthy cycle:

1. `Loading` for under 3 minutes. The file may be stale during the launch.
2. `TimerDetected`, `updatedAt` fresh every second, `timer` advancing, `focus` set.
3. `EndDetected` with `outcome` `VerifiedCompleted` and `completedReplayId` equal to `replayId`.
4. About 90 seconds of report scenes, where the file is stale. That is normal.
5. `Loading` for the next replay.

| Symptom | Meaning | Action |
| --- | --- | --- |
| Spectate pid gone | Crashed. Nothing restarts it. | Restart (section 6). |
| `services status` `3 of 4` | A role died. | Restart. |
| Stale more than 10 minutes, spectate alive | Stuck between replays. | Read section 9 logs, then restart. |
| `Loading` more than 10 minutes | Launch stuck. | Read logs, then restart. |
| `outcome` `ClientCrashed`, `ClientHung`, `LoadTimedOut`, `VersionMismatch` now and then | The app skipped that replay (retried after 30 min). | None. Repeated every replay: logs, then tell the user. |
| `outcome` `BuildNotInstalled` | Replay needs a game build that is not installed. It stays queued. | None, unless every replay does this: a patch is out; tell the user. |
| `Idle` for a long time | No replays cached. | `heroesprofile download` alive? `check heroesprofile`? Files arriving in `Data\Standard`? |
| Game shows a login form | Battle.net signed out. | Tell the user to sign in. |
| Battle.net shows Update | New patch. | Tell the user. Do not click. Then `data download --force` after they update. |
| Uploads failing, "quota" | YouTube daily quota. | Wait; it resets at midnight Pacific. |

When unsure, report what you saw (command, exit code, log lines) and leave a running stack alone.

## 9. Telemetry: the Aspire dashboard

Every role exports OpenTelemetry logs and traces over OTLP gRPC to `http://127.0.0.1:4317`. The standalone Aspire dashboard receives them. UI: `http://127.0.0.1:18888` (no login). It keeps data in memory only (about the last 10,000 log entries) and loses it when restarted. Telemetry sent while it is down is dropped.

```powershell
.\heroesreplay.exe otel status      # exit 0 when OTLP 4317 is listening
.\heroesreplay.exe otel up          # start it (needs `aspire` on PATH, section 4)
```

`services start` starts it too. Its own logs: `%LOCALAPPDATA%\HeroesReplay\logs\aspire-dashboard.log` and `.err.log`.

Read it without a browser. `--dashboard-url` is required, and use `--format Json` when parsing:

```powershell
$d = '--dashboard-url', 'http://127.0.0.1:18888', '--nologo', '--non-interactive'
aspire otel logs @d --severity Warning -n 100 --format Json                 # all roles
aspire otel logs heroesreplay-spectate @d --severity Error -n 50             # one role
aspire otel logs heroesreplay-spectate @d --search "Timer OCR" -n 50
aspire otel traces @d --has-error true -n 20                                 # failed replays
aspire otel logs @d --trace-id <traceId> -n 200                              # everything for one replay
```

Resources are named by role: `heroesreplay-spectate`, `heroesreplay-twitch`, `heroesreplay-download`, `heroesreplay-youtube`, and short-lived `heroesreplay-check` / `heroesreplay-client`. Each process shows as `<name>-<8 hex>`; the bare name matches all of them. Each replay has its own trace (`heroesreplay.replay`), shared by the spectate and twitch processes. Log records carry `ReplayId`, `Phase`, `Path`, and similar attributes, plus full exception stacks.

Without the Aspire CLI: `Invoke-RestMethod http://127.0.0.1:18888/api/telemetry/resources` and `Invoke-RestMethod "http://127.0.0.1:18888/api/telemetry/logs?limit=200"` (OTLP JSON; filter on `severityText`).

What to look for:

| Pattern | Role | Means |
| --- | --- | --- |
| "An unexpected error in the replay engine." / "Spectate hit an error and is still running." | spectate | Engine error. Once is fine; repeated means restart and report. |
| "Timer OCR is not -MM:SS or MM:SS" many times, "Could not get timer from bitmap", "No match clock after" | spectate | HUD clock unreadable: wrong interface, window not 1080p, or capture black. `check client`. |
| "Game window capture ... was empty/black" | spectate | Window minimized or no desktop session. |
| "Replay ... needs a Heroes client that is not installed" | spectate | Missing build (section 8). |
| "Game process exited" / "Game not responding" | spectate | Client crash or hang; the replay is skipped. |
| "OBS stream ... was not confirmed", "Could not set scene", "OBS recording ... failed" | spectate | Section 7. |
| "Release update did not stage" | spectate | Self-update failed; this version keeps running. |
| "Twitch chat disconnected" repeated, "EventSub reward listener stopped", prediction errors | twitch | Twitch auth or network. `check twitch`. |
| "Could not get replays from HeroesProfile", "Heroes Profile download failed" | download | Heroes Profile key or API. `check heroesprofile`. |
| "YouTube search quota is exhausted", "Could not upload" | youtube | Quota (wait) or OAuth (section 4). |
| "Connectivity ...", "Disk ... New spectating waits." | spectate | Network outage or low disk. |

If the dashboard is down, use the role console windows and the Windows Application event log (Warning and above):

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'HeroesReplay.SpectatorService', 'HeroesReplay.TwitchService', 'HeroesReplay.YouTubeService' } -MaxEvents 50 -ErrorAction SilentlyContinue | Format-List TimeCreated, ProviderName, Message
```

## 10. Updates

**Automatic.** After each replay that played to the end, the spectator asks GitHub for the latest release of `HeroesReplay/HeroesReplay`. When its tag differs from `version.txt`, it downloads `heroesreplay-win-x64.zip` to `%LOCALAPPDATA%\HeroesReplay\updates\<tag>\`, signals `services.stop`, and runs `apply-release.ps1` hidden. The script:

1. closes any `heroesreplay mcp` server (yours too; your client restarts it);
2. waits for every `heroesreplay` process to exit;
3. backs up the install to `app.previous`;
4. copies the release in, restores secrets from `C:\heroesreplay\secrets`, and copies the OBS scene and profile only if OBS is closed;
5. starts the stack again (task, `start-live.cmd`, or `services start`).

The stream goes offline for the minute or two this takes. The log is `%LOCALAPPDATA%\HeroesReplay\logs\apply-release.log`.

```powershell
.\heroesreplay.exe update check          # installed vs latest; changes nothing
Get-Content "$env:LOCALAPPDATA\HeroesReplay\logs\apply-release.log" -Tail 40
```

**Manual update**, when the user asks or the automatic one is stuck. Do it between matches.

```powershell
Set-Location C:\heroesreplay\app
$env:HEROES_REPLAY_ENV = 'prod'
$stage = Join-Path $env:TEMP 'heroesreplay-release'
Remove-Item $stage, "$stage.zip" -Recurse -Force -ErrorAction SilentlyContinue
Invoke-WebRequest https://github.com/HeroesReplay/HeroesReplay/releases/latest/download/heroesreplay-win-x64.zip -OutFile "$stage.zip"
Expand-Archive "$stage.zip" -DestinationPath $stage -Force
powershell -NoProfile -ExecutionPolicy Bypass -File .\ensure-secrets.ps1
.\heroesreplay.exe services stop
powershell -NoProfile -ExecutionPolicy Bypass -File "$stage\apply-release.ps1" -InstallDir C:\heroesreplay\app -StagingDir $stage
Get-Content .\version.txt
```

Run the staged `apply-release.ps1`, not the installed one. If it prints "Previous install is still inside the stabilization window", the current install has not run 2 minutes since `services start`: it restarts the current build, so wait and run the last command again.

Then section 7 and section 8. A new release also replaces this file; reread it.

**Rollback:** `services stop`, `robocopy C:\heroesreplay\app.previous C:\heroesreplay\app /MIR`, `ensure-secrets.ps1`, start. Tell the user which version you rolled back from and why.

## 11. Start on logon

Without this, a reboot leaves production down until someone starts it. With the user's OK, from an elevated PowerShell as the streaming user:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\heroesreplay\app\register-autostart.ps1
```

It writes `%LOCALAPPDATA%\HeroesReplay\start-live.cmd` (sets `HEROES_REPLAY_ENV=prod`, runs `services start`) and the scheduled task `HeroesReplay-live` (at that user's logon plus 1 minute, interactive, highest privileges). The updater also uses the task to restart. Windows auto-logon is the user's decision. `-Remove` undoes it.

## 12. First install

Ask the user first. Needs everything in section 4 that cannot be installed by the release: Heroes of the Storm, Battle.net signed in, OBS, the stream key, the .NET runtime.

1. Folders:
   ```powershell
   'C:\heroesreplay\app', 'C:\heroesreplay\secrets', 'C:\heroesreplay\Replays', 'C:\heroesreplay\Data\Standard', 'C:\heroesreplay\Data\Requests', 'C:\heroesreplay\Data\Contexts', 'C:\heroesreplay\Data\HeroesData' | % { New-Item -ItemType Directory -Force $_ | Out-Null }
   ```
2. Release: download `https://github.com/HeroesReplay/HeroesReplay/releases/latest/download/heroesreplay-win-x64.zip` and extract it into `C:\heroesreplay\app`. Not into a git checkout.
3. Secrets: `ensure-secrets.ps1` (from the backup folder, or 1Password with `OP_SERVICE_ACCOUNT`).
4. Game data: `.\heroesreplay.exe data download`.
5. OBS (closed):
   ```powershell
   New-Item -ItemType Directory -Force "$env:APPDATA\obs-studio\basic\scenes", "$env:APPDATA\obs-studio\basic\profiles\HeroesReplay" | Out-Null
   Copy-Item .\obs\Default.json "$env:APPDATA\obs-studio\basic\scenes\HeroesReplay.json"
   Copy-Item .\obs\Default\basic.ini "$env:APPDATA\obs-studio\basic\profiles\HeroesReplay\basic.ini"
   ```
   Then the user opens OBS once: enables the WebSocket server (port 4455, no auth), selects profile HeroesReplay, sets Stream > Twitch with the key, and closes OBS. `services start` rewrites asset paths in the collection while OBS is closed.
6. Client: Heroes closed, `.\heroesreplay.exe client configure`.
7. Aspire CLI (section 4).
8. Preflight (section 5), start (section 6), stream (section 7).
9. YouTube: the first upload opens a Google consent page; the user completes it.
10. Offer section 11.

## 13. Command reference

All from `C:\heroesreplay\app` with `HEROES_REPLAY_ENV=prod`. `--help` on any command shows its options.

**Routine (read-only or safe):**

| Command | Use |
| --- | --- |
| `services status` | Running roles and the spectator snapshot. Exit 1 if some roles died. |
| `check` / `check config|heroesprofile|obs|twitch|client|connectivity|battlenet|twitch-extension` | Connectivity and setup checks. |
| `data status` | heroes-data2 cache build. |
| `update check` | Installed vs latest release. |
| `client status` | Windowed 1080p and AhliObs. |
| `otel status` | Is the dashboard receiving telemetry. |
| `twitch rewards list` | Channel-point rewards (read). |
| `heroesprofile patch-index` | First replay id of the current patch (read). |
| `mcp` | Stdio MCP server for agents (section 14). |

**Changes live state. Only when needed or asked:**

| Command | Use |
| --- | --- |
| `services start` / `services stop` | Section 6. |
| `data download [--force]` | Fetch heroes-data2. `--force` after a game patch, then restart. |
| `client configure` | Heroes closed only. |
| `otel up` / `otel down` | Start or stop the dashboard. |
| `check timer` | Reads the running game's memory for up to 35 seconds. |
| `twitch say --message "..."` | Posts in live chat. Only with the user's exact text. |
| `twitch rewards submit` / `remove-unranked-draft` | Changes channel-point rewards. `twitch connect` already syncs them at start. |
| `heroesprofile patch-index --write` | Rewrites `MinReplayId` in the install's `appsettings.json`. Restart afterwards. |
| `youtube library --once` | One playlist filing pass. The uploader already does this. |

**Started by `services start`. Never run by hand:** `spectate heroesprofile`, `twitch connect`, `heroesprofile download`, `youtube uploader`.

**Internal to the updater:** `update release-health`, `update preserve-min-replay-id`.

**Never on production:**

- `spectate file`: a second spectator, and with prod settings it can stream and self-update.
- `twitch predictions test`: creates a real prediction on the live channel.
- `twitch rewards test`: posts in live chat and can queue a real request.
- `twitch rewards generate`, `calculators *`: development tools.

## 14. MCP servers in this folder

`.mcp.json` (Claude Code) and `.grok/config.toml` (Grok) register two servers when the agent starts in `C:\heroesreplay\app`:

| Server | Tools |
| --- | --- |
| `heroesreplay` (`heroesreplay.exe mcp`, prod settings) | `get_spectator_status`, `get_current_focus`, `check_config`, `check_heroesprofile`, `check_obs`, `check_twitch`, `check_battlenet`. Reads `status.json`; it is not the spectator. The updater closes it while it replaces the install. |
| `aspire` (`aspire agent mcp --dashboard-url http://127.0.0.1:18888`) | `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. Needs `aspire` on PATH and the dashboard running. |

If a server is missing or not connected, the CLI commands above give the same answers.
