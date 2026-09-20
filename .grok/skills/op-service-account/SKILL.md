---
name: op-service-account
description: >
  1Password CLI for HeroesReplay secrets via the OP_SERVICE_ACCOUNT
  service-account token. Use when cloning onto a new machine, filling
  appsettings.secrets.json, reading op:// URIs, Heroes Profile or Twitch
  credentials, or /op-service-account.
---

# 1Password service account

HeroesReplay development uses a **1Password service account**, not desktop `op signin`. After a git clone on a new PC, run the bootstrap below before `check` or spectate.

## Token

User env **`OP_SERVICE_ACCOUNT`** holds the token (`ops_…`). The official CLI only reads **`OP_SERVICE_ACCOUNT_TOKEN`**. Map before every `op` call:

```powershell
if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $t = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT', 'User')
    if (-not $t) { $t = $env:OP_SERVICE_ACCOUNT }
    if ($t) { $env:OP_SERVICE_ACCOUNT_TOKEN = $t }
}
```

`SecretResolver` does the same when resolving `op://` values. Never print the token. Never commit it.

This account is **SERVICE_ACCOUNT**. It cannot see vault `Private`. Do not use desktop Allow prompts when this token is set.

## New machine (clone / pull)

1. Install Git, .NET 10 SDK, and 1Password CLI (`winget install --exact Git.Git Microsoft.DotNet.SDK.10 AgileBits.1Password.CLI`). Open a new shell so `op` is on PATH.
2. Clone into **`C:\heroesreplay\HeroesReplay`** (required: OBS `obs/Default.json` uses that path). `git clone https://github.com/HeroesReplay/HeroesReplay.git C:\heroesreplay\HeroesReplay`
3. Set the service-account token **once** at User scope (paste the `ops_` value; do not commit it):

```powershell
[Environment]::SetEnvironmentVariable('OP_SERVICE_ACCOUNT', 'ops_…', 'User')
```

4. New PowerShell, then from the repo root:

```powershell
pwsh -File tools/bootstrap-workstation.ps1
op whoami
dotnet tool restore
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check config
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check heroesprofile
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check twitch
```

`bootstrap-workstation.ps1` creates `C:\heroesreplay\Replays`, `Data\Standard`, `Data\Contexts`, copies the OBS HeroesReplay collection/profile, then runs `fill-secrets-from-op.ps1`. Install Battle.net with `winget install Blizzard.BattleNet --location C:\heroesreplay\Battle.net`. Quit HotS and run `heroesreplay client configure` for AhliObs + windowed 1080p. Report only `op whoami` user type and secret **lengths**. Layout: `AGENTS.md` Environments.

## Vault

Name: `Heroes Replay` (id `fk7tudovwzuaa64lvomn6rxwtq`). Quote `op://` URIs that contain spaces.

| Secret | URI |
| --- | --- |
| Heroes Profile v1 Bearer | `op://Heroes Replay/Heroes Profile API Key/password` |
| Twitch access token | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Access Token` |
| Twitch client id | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Client Id` |
| Twitch refresh token | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Refresh Token` |
| Stream key | `op://Heroes Replay/Twitch SaltySadism/stream key` |
| YouTube API key | `op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/API Key` (item YouTube Uploader) |
| YouTube OAuth client id | `op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Client ID` |
| YouTube OAuth client secret | `op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Client Secret` |
| YouTube GCP project id | `op://Heroes Replay/xrstilaqn2jygtuwwde346ozwm/Project ID` |

Items with `(` in the title: use the item UUID, not the name.

```powershell
op read "op://Heroes Replay/Heroes Profile API Key/password"
op whoami   # User Type: SERVICE_ACCOUNT
op vault list
```

Twitch Helix Predictions need `channel:manage:predictions` on the access token. `check twitch` reports whether that scope is present.

## App secrets file

Live values go in gitignored `src/HeroesReplay.CLI/appsettings.secrets.json` (Twitch Helix, Heroes Profile v1 Bearer, YouTube API key). `fill-secrets-from-op.ps1` also writes `C:\heroesreplay\Data\client_secrets.json` (Google OAuth desktop client for `youtube uploader`). Template: `appsettings.secrets.example.json`. Copy into CLI `bin/...` when running the exe. Env override prefix `HEROES_REPLAY_`. Do not grep or echo that file for secret values; report `source=literal|op` and length only. The Google account password on the YouTube Uploader item is not used; upload uses OAuth.
