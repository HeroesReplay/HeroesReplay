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
- Credentials: `appsettings.secrets.json` (`Twitch:AccessToken`, `ClientId`, `Account`). Never commit it.

## Helix

Channel-point methods are `*Async`: `GetCustomRewardAsync`, `CreateCustomRewardsAsync`, `UpdateCustomRewardAsync`, `DeleteCustomRewardAsync`.

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
```

`check twitch` is Helix `GetUsers` only (needs AccessToken + ClientId). `twitch connect` blocks. Command map: skill `heroes-replay-cli`.
