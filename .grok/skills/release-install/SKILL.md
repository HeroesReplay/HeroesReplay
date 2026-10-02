---
name: release-install
description: >
  Move a HeroesReplay machine from a git clone and local build onto the GitHub
  Release zip. Use when installing production, migrating off source, or /release-install.
---

# Release install

Production runs the zip attached to a GitHub Release. It does not clone the repo and it does not run `dotnet build`. ASA-SERVER keeps compiling from the `develop` worktree. A push to `master` runs `.github/workflows/release.yml`: Unit and Smoke tests, `tools/package-release.ps1` (publish `win-x64`, `obs/bundle.manifest`, `version.txt`, no secrets or `service.json`), `tools/verify-release.ps1`, then upload `heroesreplay-win-x64.zip`. `ci.yml` builds and checks the same zip on every pull request and push. The check extracts it, runs `heroesreplay.exe --help`, requires every bundle asset and what the update reads, and fails on `service.json`, `appsettings.secrets.json`, `client_secrets.json`, or a token file. GitVersion names the `master` release. The tag and `version.txt` are `v` plus that version, such as `v1.0.0`. `develop` stays a pre-release (`1.1.0-alpha.N`) and is not published.

## Layout

| Path | What |
| --- | --- |
| `C:\heroesreplay\app` | The published exe, `appsettings.json`, `appsettings.prod.json`, `apply-release.ps1`, `version.txt`, and `obs\`. This is the directory that gets replaced. The update keeps the previous copy at `C:\heroesreplay\app.previous`. |
| `C:\heroesreplay\Data` | Queue, replays, `spectated-ids.txt`, `requests.json`, contexts. Not in the zip. An update must not delete or rewrite it. |
| `C:\heroesreplay\secrets\appsettings.secrets.json` | Tokens. Not in the zip. The helper copies it back to `appsettings.secrets.json` beside the exe. |

`HEROES_REPLAY_ENV=prod` when starting services from that folder. `Release:Enabled` is true only in `appsettings.prod.json`. A source build under `src` or `worktrees` never replaces itself.

## First install

Stop any source-built `heroesreplay` and close Heroes of the Storm. OBS should be closed so the scene collection can be copied.

1. Download `heroesreplay-win-x64.zip` from the latest release.
2. Extract it to `C:\heroesreplay\app`.
3. Put secrets at `C:\heroesreplay\secrets\appsettings.secrets.json`.
4. On the stream PC only, run `heroesreplay obs arm`. Twitch ingest needs this machine-local arm (`%LOCALAPPDATA%\HeroesReplay\stream-armed`) as well as `OBS:StreamingEnabled` from `appsettings.prod.json`. A first install has no previous install to migrate from, so nothing arms it for you.
5. From `C:\heroesreplay\app`, run `heroesreplay services start` with `HEROES_REPLAY_ENV=prod`.

Do not point the install at the git worktree. The OBS profile (`basic.ini`) belongs to the machine: if `%APPDATA%\obs-studio\basic\profiles\{OBS:ProfileName}\basic.ini` already exists it is kept. Otherwise copy `app\obs\Default\basic.ini` there as a starting template and tune it in OBS.

## Later updates

After a replay finishes, the spectator compares `version.txt` with the latest release. A newer zip is downloaded and prepared under `%LOCALAPPDATA%\HeroesReplay\updates\<version>` (secrets and the higher `MinReplayId` carried over), the spectator writes `services.stop` so every service exits, and `apply-release.ps1` waits until `heroesreplay.exe` has exited. When `app.previous` exists it runs `update release-health`. If the previous install is still inside the stabilization window, `app.previous` stays the rollback and the current install is not backed up; the update still installs. Otherwise it backs up `C:\heroesreplay\app` to `app.previous`. Either way it copies the new files over it, copies secrets back, and starts the stack again: scheduled task `HeroesReplay-live` if it exists, else `%LOCALAPPDATA%\HeroesReplay\start-live.cmd`, else `heroesreplay services start`. A failed copy restores `app.previous`. The next replay is the next unplayed file.

OBS files: after the copy, the new exe runs `update install-obs`, which reads `OBS:SceneCollectionName` and `OBS:ProfileName` from the install. If OBS is open, scene files stay in `app\obs` and are not copied over the live collection. If OBS is closed, `Default.json` replaces `scenes\{collection}.json`. The profile `basic.ini` is copied only when the machine has no profile of that name; an existing profile is kept and the log says so. `service.json` is never copied.

Stream arm migration: before the files are replaced, the staged exe runs `update migrate-stream-arm --previous C:\heroesreplay\app`. It reads the effective settings of the install being replaced (`appsettings.json`, secrets, `appsettings.{HEROES_REPLAY_ENV or prod}.json`, `HEROES_REPLAY_` variables). When `OBS:StreamingEnabled` is true there and the machine is not armed, it arms the machine once, so the update that introduces the arm keeps production live. It never arms when `OBS:StreamingEnabled` is false. It writes `%LOCALAPPDATA%\HeroesReplay\stream-arm.migrated` and never runs again, so a later `obs disarm` survives updates.

`apply-release.ps1` runs hidden. It appends what it did (OBS files kept or copied, the arm decision) to `%LOCALAPPDATA%\HeroesReplay\logs\apply-release.log`.

`heroesreplay update check` prints the installed version and the latest tag. It does not download or restart.

## Agent on the production machine

The zip does not yet include an MCP config. A Grok session only sees a server when its working directory, or `~/.grok/config.toml`, names it. The repo `.grok/config.toml` runs `dotnet run --project src/HeroesReplay.CLI`, which does not exist in the published folder.

Two stdio servers, both already installed separately from the zip:

| Server | Command | What the agent gets |
| --- | --- | --- |
| Spectator | `C:\heroesreplay\app\heroesreplay.exe mcp` with `HEROES_REPLAY_ENV=prod` | `get_spectator_status`, `get_current_focus`, `check_twitch`, `check_obs`, `check_heroesprofile`, `check_config`, `check_battlenet`, and the read-only `obs_inspect`, `obs_validate`, `obs_screenshot`. Reads `%LOCALAPPDATA%\HeroesReplay\status.json`. A second process from the one playing the match. No tool changes OBS. |
| Aspire | `aspire agent mcp --dashboard-url http://127.0.0.1:18888` | On CLI 13.5.4, dashboard-only mode: `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. The dashboard UI stays `http://127.0.0.1:18888`. `list_resources` and start/stop need an AppHost, which this app does not run. |

`aspire` is the machine dotnet tool (`aspire.cli` 13.5.4), not a file inside the zip. The release should still ship `.mcp.json` next to the exe so an agent started in `C:\heroesreplay\app` finds both commands. `aspire agent init` writes that file for a source tree; it does not know the published exe path.
