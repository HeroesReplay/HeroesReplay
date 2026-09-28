---
name: release-install
description: >
  Move a HeroesReplay machine from a git clone and local build onto the GitHub
  Release zip. Use when installing production, migrating off source, or /release-install.
---

# Release install

Production runs the zip attached to a GitHub Release. It does not clone the repo and it does not run `dotnet build`. ASA-SERVER keeps compiling from the `develop` worktree. A push to `master` runs `.github/workflows/release.yml`, which tests, publishes `win-x64`, and uploads `heroesreplay-win-x64.zip`. GitVersion names the `master` release. The tag and `version.txt` are `v` plus that version, such as `v1.0.0`. `develop` stays a pre-release (`1.1.0-alpha.N`) and is not published.

## Layout

| Path | What |
| --- | --- |
| `C:\heroesreplay\app` | The published exe, `appsettings.json`, `appsettings.prod.json`, `apply-release.ps1`, and `obs\`. This is the directory that gets replaced. |
| `C:\heroesreplay\Data` | Queue, replays, `spectated-ids.txt`, `requests.json`, contexts. Not in the zip. An update must not delete or rewrite it. |
| `C:\heroesreplay\secrets\appsettings.secrets.json` | Tokens. Not in the zip. The helper copies it back to `appsettings.secrets.json` beside the exe. |

`HEROES_REPLAY_ENV=prod` when starting services from that folder. `Release:Enabled` is true only in `appsettings.prod.json`. A source build under `src` or `worktrees` never replaces itself.

## First install

Stop any source-built `heroesreplay` and close Heroes of the Storm. OBS should be closed so the scene collection can be copied.

1. Download `heroesreplay-win-x64.zip` from the latest release.
2. Extract it to `C:\heroesreplay\app`.
3. Put secrets at `C:\heroesreplay\secrets\appsettings.secrets.json`.
4. From `C:\heroesreplay\app`, run `heroesreplay services start` with `HEROES_REPLAY_ENV=prod`.

Do not point the install at the git worktree.

## Later updates

After a replay finishes, the spectator compares `version.txt` with the latest release. A newer zip is downloaded, `services stop` runs, and `apply-release.ps1` waits until `heroesreplay.exe` has exited, swaps `C:\heroesreplay\app`, copies secrets back, and starts services again. The next replay is the next unplayed file. If OBS is open, scene files stay in `app\obs` and are not copied over the live collection. If OBS is closed, `Default.json` and `basic.ini` are copied. `service.json` is never copied.

`heroesreplay update check` prints the installed version and the latest tag. It does not download or restart.

## Agent on the production machine

The zip does not yet include an MCP config. A Grok session only sees a server when its working directory, or `~/.grok/config.toml`, names it. The repo `.grok/config.toml` runs `dotnet run --project src/HeroesReplay.CLI`, which does not exist in the published folder.

Two stdio servers, both already installed separately from the zip:

| Server | Command | What the agent gets |
| --- | --- | --- |
| Spectator | `C:\heroesreplay\app\heroesreplay.exe mcp` with `HEROES_REPLAY_ENV=prod` | `get_spectator_status`, `get_current_focus`, `check_twitch`, `check_obs`, `check_heroesprofile`, `check_config`. Reads `%LOCALAPPDATA%\HeroesReplay\status.json`. A second process from the one playing the match. |
| Aspire | `aspire agent mcp --dashboard-url http://127.0.0.1:18888` | On CLI 13.5.4, dashboard-only mode: `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. The dashboard UI stays `http://127.0.0.1:18888`. `list_resources` and start/stop need an AppHost, which this app does not run. |

`aspire` is the machine dotnet tool (`aspire.cli` 13.5.4), not a file inside the zip. The release should still ship `.mcp.json` next to the exe so an agent started in `C:\heroesreplay\app` finds both commands. `aspire agent init` writes that file for a source tree; it does not know the published exe path.
