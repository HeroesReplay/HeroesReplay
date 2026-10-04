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

When a match ends, before the next replay is picked, the spectator compares `version.txt` with the latest release (the GitHub lookup gives up after 30 s and the current build keeps going). A tag listed in `%LOCALAPPDATA%\HeroesReplay\updates\skipped-releases.txt` is never staged again. A newer zip is downloaded and prepared under `%LOCALAPPDATA%\HeroesReplay\updates\<version>` (secrets and the higher `MinReplayId` carried over) while the report scenes play. A staged release launches no next replay: the report plays out, OBS shows the waiting scene, and a live stream stays up on it through the install (with no `OBS:WaitingSceneName` the stream stops, as on any stop); the new stack finds the stream live and keeps it. Then the spectator starts `apply-release.ps1 -Version <tag>` (plus `-Supervise` when a supervisor runs, since the stop file ends it too), writes `services.stop` so every service exits, and the script waits until `heroesreplay.exe` has exited. Every update first mirrors `C:\heroesreplay\app` into `app.previous` (that build just finished a replay, so it is the known-good rollback); if that backup fails, nothing is installed and the current install starts again. The script then copies the new files over the install, copies secrets back, and starts the stack again: scheduled task `HeroesReplay-live` if it exists (`heroesreplay services install-task --environment prod` creates it, supervised, with no administrator rights), else `%LOCALAPPDATA%\HeroesReplay\start-live.cmd`, else `heroesreplay services start --supervise`. The stack always comes back supervised: when the task or `start-live.cmd` starts it without a supervisor, the script waits until every role is ready (up to 5 min) and starts `heroesreplay services supervise`. A failed copy restores `app.previous`. The new build launches the next unplayed file.

Health gate: the new exe runs `update release-health --wait`. Healthy means every role in `services.json` runs with a fresh `ready\<nonce>.json` heartbeat from a process started after the install, and spectate reached a match clock (or the award screen) after it, within `Release:HealthWindow` (20 min). Spectate counts how each replay session ended (`sessionOutcomes` in its heartbeat), so when the window closes without a clock the gate splits three ways:

- Healthy (exit 0): mirrors the install into `app.previous`.
- Inconclusive (exit 4): every role is up, but spectate had nothing it could play: no session (empty queue, Heroes Profile or network outage) or only `BuildNotInstalled`, `VersionMismatch`, or `RegionUnavailable`. The install stays, `app.previous` is not refreshed, the tag is not skipped, and the log says `INCONCLUSIVE`.
- Unhealthy (exit 2): a role is down or stale, or spectate tried a replay and none reached a clock (`LoadTimedOut`, `ClientCrashed`, `ClientHung`, `Canceled`, `Error`), or the check crashed or hung.

Unhealthy appends the tag to `skipped-releases.txt`, runs `services stop` (then kills any `heroesreplay` left), mirrors `app.previous` back, keeps the higher `MinReplayId`, runs `update install-obs` from the restored exe, and starts the stack the same way. A `services.stop` during the window is no verdict: nothing is rolled back. Delete a line from `skipped-releases.txt` to allow that release again. Every step is in `logs\apply-release.log`.

OBS files: after the copy, the new exe runs `update install-obs`, which reads `OBS:SceneCollectionName` and `OBS:ProfileName` from the install. If OBS is open, scene files stay in `app\obs` and are not copied over the live collection; when the template changed, the new stack's first replay swaps it in live through the spare collection `{collection}-next`, without stopping the stream (`OBS:LiveCollectionSwap`, `docs/obs-operations.md`). If OBS is closed, `Default.json` replaces `scenes\{collection}.json`. The profile `basic.ini` is copied only when the machine has no profile of that name; an existing profile is kept and the log says so. `service.json` is never copied.

Stream arm migration: before the files are replaced, the staged exe runs `update migrate-stream-arm --previous C:\heroesreplay\app`. It reads the effective settings of the install being replaced (`appsettings.json`, secrets, `appsettings.{HEROES_REPLAY_ENV or prod}.json`, `HEROES_REPLAY_` variables). When `OBS:StreamingEnabled` is true there and the machine is not armed, it arms the machine once, so the update that introduces the arm keeps production live. It never arms when `OBS:StreamingEnabled` is false. It writes `%LOCALAPPDATA%\HeroesReplay\stream-arm.migrated` and never runs again, so a later `obs disarm` survives updates.

`apply-release.ps1` runs hidden. It appends what it did (OBS files kept or copied, the arm decision, the health verdict, a rollback) to `%LOCALAPPDATA%\HeroesReplay\logs\apply-release.log`.

`heroesreplay update check` prints the installed version and the latest tag. It does not download or restart.

## Agent on the production machine

The zip does not yet include an MCP config. A Grok session only sees a server when its working directory, or `~/.grok/config.toml`, names it. The repo `.grok/config.toml` runs `dotnet run --project src/HeroesReplay.CLI`, which does not exist in the published folder.

Two stdio servers, both already installed separately from the zip:

| Server | Command | What the agent gets |
| --- | --- | --- |
| Spectator | `C:\heroesreplay\app\heroesreplay.exe mcp` with `HEROES_REPLAY_ENV=prod` | `get_spectator_status`, `get_current_focus`, `check_twitch`, `check_obs`, `check_heroesprofile`, `check_config`, `check_battlenet`, and the read-only `obs_inspect`, `obs_validate`, `obs_screenshot`. Reads `%LOCALAPPDATA%\HeroesReplay\status.json`. A second process from the one playing the match. No tool changes OBS. |
| Aspire | `aspire agent mcp --dashboard-url http://127.0.0.1:18888` | On CLI 13.5.4, dashboard-only mode: `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. The dashboard UI stays `http://127.0.0.1:18888`. `list_resources` and start/stop need an AppHost, which this app does not run. |

`aspire` is the machine dotnet tool (`aspire.cli` 13.5.4), not a file inside the zip. The release should still ship `.mcp.json` next to the exe so an agent started in `C:\heroesreplay\app` finds both commands. `aspire agent init` writes that file for a source tree; it does not know the published exe path.
