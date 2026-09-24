---
name: release-install
description: >
  Move a HeroesReplay machine from a git clone and local build onto the GitHub
  Release zip. Use when installing production, migrating off source, or /release-install.
---

# Release install

Production runs the zip attached to a GitHub Release. It does not clone the repo and it does not run `dotnet build`. ASA-SERVER keeps compiling from the `develop` worktree. A push to `master` runs `.github/workflows/release.yml`, which tests, publishes `win-x64`, and uploads `heroesreplay-win-x64.zip`. The tag and `version.txt` are `r` plus the commit SHA. GitHub rejects a tag that is only 40 hex characters.

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

`heroesreplay update check` prints the installed SHA and the latest tag. It does not download or restart.
