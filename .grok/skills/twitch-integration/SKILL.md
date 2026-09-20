---
name: twitch-integration
description: >
  TwitchLib chat, Helix API, and channel-point rewards used by HeroesReplay.
  Use when changing TwitchBot, rewards, FakeTwitchClient, check twitch, or /twitch-integration.
---

# Twitch integration

## Stack

- Metapackage `TwitchLib` 3.5.x (`TwitchLib.Client` ~3.3, `TwitchLib.Api` Helix).
- Dry-run: `CaptureMethod.None` still swaps in `FakeTwitchClient` / `FakeTwitchBot`. Keep the fake implementing the current `ITwitchClient` surface.
- Credentials: `appsettings.secrets.json` (`Twitch:AccessToken`, `ClientId`, `Account`). Fill with skill `op-service-account` / `tools/fill-secrets-from-op.ps1`. Never commit it. Predictions need `channel:manage:predictions` on that access token (`check twitch` reports it).

## Helix

Channel-point methods are `*Async`: `GetCustomRewardAsync`, `CreateCustomRewardsAsync`, `UpdateCustomRewardAsync`, `DeleteCustomRewardAsync`.

Helix Predictions (Blue/Red who-wins): `CreatePredictionAsync` when the match clock is detected, `EndPredictionAsync` (RESOLVED/CANCELED) when the spectate session ends. Team 0 = Blue (left), team 1 = Red (right). Requires `channel:manage:predictions`. Toggle `Twitch:EnablePredictions`. Window `Twitch:PredictionWindow` (clamped 30s–1800s). Dry-run and `CaptureMethod.None` skip Helix.

## Do not grow PubSub rewards

`ITwitchPubSub.ListenToRewards` / `OnRewardRedeemed` are obsolete (undocumented topic). Do not add new listeners. New redemption work should target **EventSub** (`channel.channel_points_custom_reward_redemption.add`), not PubSub.

Chat (`TwitchClient`) and Helix remain fine.

Chat (during `spectate`, chatbot enabled), matching Icy Veins observer hotkeys:
- `!talents` → Ctrl+1 talent panel
- `!stats` → Ctrl+2 stats panel
Each shows for `Spectate:StatsPanelShowDuration` (default 10s) with `StatsPanelCooldown` (default 2 minutes, independent per panel). Do not auto-cycle KDA/XP/stats; talents still open automatically at talent times.

## CLI

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check twitch
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch connect
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards generate
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards submit
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch predictions test --outcome Blue
```

`twitch predictions test` creates a 30s Blue/Red Helix prediction then resolves (`Blue`/`Red`) or `cancel`. Watch it on the Twitch creator dashboard. Spectate does the same at TimerDetected and at session end from `Player.IsWinner`.

`check twitch` is Helix `GetUsers` plus `GetPredictions` when `EnablePredictions` is true. `twitch connect` blocks. Command map: skill `heroes-replay-cli`.
