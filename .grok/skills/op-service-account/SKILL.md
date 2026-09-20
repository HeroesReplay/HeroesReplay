---
name: op-service-account
description: >
  1Password CLI for HeroesReplay secrets via the OP_SERVICE_ACCOUNT
  service-account token. Use when reading op:// URIs, appsettings.secrets.json,
  Heroes Profile or Twitch credentials, or /op-service-account.
---

# 1Password service account

HeroesReplay development uses a **1Password service account**, not desktop `op signin`.

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

## Vault

Name: `Heroes Replay` (id `fk7tudovwzuaa64lvomn6rxwtq`). Quote `op://` URIs that contain spaces.

| Secret | URI |
| --- | --- |
| Heroes Profile v1 Bearer | `op://Heroes Replay/Heroes Profile API Key/password` |
| Twitch access token | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Access Token` |
| Twitch client id | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Client Id` |
| Twitch refresh token | `op://Heroes Replay/Twitch SaltySadism/twitchtokengenerator/Refresh Token` |
| Stream key | `op://Heroes Replay/Twitch SaltySadism/stream key` |

Items with `(` in the title: use the item UUID, not the name.

```powershell
op read "op://Heroes Replay/Heroes Profile API Key/password"
op whoami   # User Type: SERVICE_ACCOUNT
op vault list
```

## App secrets file

Live values go in gitignored `src/HeroesReplay.CLI/appsettings.secrets.json` (literals, same as Twitch/AWS). Template: `appsettings.secrets.example.json`. Copy into CLI `bin/...` when running the exe. Env override prefix `HEROES_REPLAY_`. Do not grep or echo that file for secret values; report `source=literal|op` and length only.
