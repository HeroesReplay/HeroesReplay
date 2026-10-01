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
| `C:\heroesreplay\app` | The published exe, `appsettings.json`, `appsettings.prod.json`, `apply-release.ps1`, `ensure-secrets.ps1`, the agent runbook (`AGENTS.md`, `CLAUDE.md`, `.mcp.json`, `.grok\config.toml`), and `obs\`. This is the directory that gets replaced. |
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

The zip carries everything an agent needs to run production without the repo. The sources live in `deploy/production/` and `tools/`, and `HeroesReplay.CLI.csproj` publishes them (publish only, so a dev bin never carries the prod runbook):

| In the install | Source | What |
| --- | --- | --- |
| `AGENTS.md` | `deploy/production/AGENTS.md` | Production runbook: preflight, start, status, stop, restart, update, rollback, first install. Grok and other agents read `AGENTS.md`. |
| `CLAUDE.md` | `deploy/production/CLAUDE.md` | `@AGENTS.md`, so Claude Code loads the same runbook. |
| `.mcp.json` | `deploy/production/.mcp.json` | Claude Code MCP servers. |
| `.grok/config.toml` | `deploy/production/.grok/config.toml` | Grok MCP servers. |
| `ensure-secrets.ps1` | `tools/ensure-secrets.ps1` | Puts `appsettings.secrets.json` beside the exe: install file, then `C:\heroesreplay\secrets`, then 1Password. Validates the Twitch token and writes `Twitch:GrantedScopes` from it (the Twitch role needs them). The newer of install and backup wins. Prints names, lengths, and scopes only. |
| `fill-secrets-from-op.ps1` | `tools/fill-secrets-from-op.ps1` | Without a git clone it writes beside the exe, or to `-Destination`. |
| `register-autostart.ps1` | `tools/register-autostart.ps1` | Writes `start-live.cmd` and the `HeroesReplay-live` logon task that `apply-release.ps1` restarts through. Nothing else creates them. |

Start the agent in `C:\heroesreplay\app`. When the runbook changes, merge to `master`; the next self-update replaces it on the machine.

Both MCP configs register two stdio servers:

| Server | Command | What the agent gets |
| --- | --- | --- |
| Spectator | `C:\heroesreplay\app\heroesreplay.exe mcp` with `HEROES_REPLAY_ENV=prod` | `get_spectator_status`, `get_current_focus`, `check_twitch`, `check_obs`, `check_heroesprofile`, `check_config`, `check_battlenet`. Reads `%LOCALAPPDATA%\HeroesReplay\status.json`. A second process from the one playing the match. |
| Aspire | `aspire agent mcp --dashboard-url http://127.0.0.1:18888` | On CLI 13.5.4, dashboard-only mode: `list_structured_logs`, `list_traces`, `list_trace_structured_logs`. `list_resources` and start/stop need an AppHost, which this app does not run. |

`aspire` is the machine dotnet tool (`aspire.cli` 13.5.4), not a file inside the zip.
