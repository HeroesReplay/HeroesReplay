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

Helix Predictions (Blue/Red who-wins): `twitch connect` calls `CreatePredictionAsync` when `status.json` phase is `TimerDetected`, and `EndPredictionAsync` (RESOLVED/CANCELED) from the completion fields when the session ends. Team 0 = Blue (left), team 1 = Red (right). Requires `channel:manage:predictions`. Toggle `Twitch:EnablePredictions`. Window `Twitch:PredictionWindow` (clamped 30s–1800s). Dry-run and `CaptureMethod.None` skip Helix. The spectator does not call Helix.

## Do not grow PubSub rewards

`ITwitchPubSub.ListenToRewards` / `OnRewardRedeemed` are obsolete (undocumented topic). Do not add new listeners. New redemption work should target **EventSub** (`channel.channel_points_custom_reward_redemption.add`), not PubSub.

Run `twitch rewards *` from the CLI output directory (so `Assets/Maps.json` loads) or `dotnet run --project src/HeroesReplay.CLI --no-launch-profile` with cwd the repo if Maps.json is copied.

Chat reconnects with backoff on disconnect and rejoins the channel. PubSub reconnects on close/error. `Client_OnDisconnected` must not call `Connect()` in a tight loop.

Helix can only **update** channel-point rewards created with the same Client-Id as the current token. Older twitchtokengenerator rewards 403 on PATCH; titles still match for PubSub redemptions. `twitch rewards test --title` runs the handler locally without a viewer redeem.

Chat (`TwitchClient`) and Helix remain fine.

Chat (during `twitch connect`, chatbot enabled), matching Icy Veins observer hotkeys. The request is written to `%LOCALAPPDATA%\HeroesReplay\panel-requests.json`. The spectator process consumes it:
- `!talents` → Ctrl+1 talent panel
- `!stats` → Ctrl+2 stats panel
Each shows for `Spectate:StatsPanelShowDuration` (default 10s) with `StatsPanelCooldown` (default 2 minutes, independent per panel). Do not auto-cycle KDA/XP/stats; talents still open automatically at talent times.

## CLI

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check twitch
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch connect
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards generate
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards submit
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards list
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards remove-unranked-draft
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards test --title "Random (SL)"
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch predictions test --outcome Blue
```

`twitch predictions test` creates a 30s Blue/Red Helix prediction then resolves (`Blue`/`Red`) or `cancel`. Watch it on the Twitch creator dashboard.

`twitch connect` opens the real match prediction. It watches `%LOCALAPPDATA%\HeroesReplay\status.json` and does not call into the spectator. Phase `TimerDetected` opens Blue/Red for `map`. When the session ends, the spectator writes `completedReplayId`, `completedAt`, and `completedWinnerTeam` (0 blue, 1 red, null cancels) and leaves them in place while the next replay loads. The spectator process does not call Helix.

After a resolved prediction, `PredictionReportWriter` writes `Data/prediction-report.html` and updates `Data/prediction-streaks.json`. Helix only returns `top_predictors` for each outcome, not every viewer. Winners increment a streak; losers reset to 0. The same prediction id is not applied twice. OBS scene `prediction-report` shows that file for 20 seconds at the start of the end-of-match report cycle, and is skipped when the file is not there yet.

`check twitch` is Helix `GetUsers` plus `GetPredictions` when `EnablePredictions` is true. `twitch connect` blocks. Command map: skill `heroes-replay-cli`.
