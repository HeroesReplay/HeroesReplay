---
name: twitch-integration
description: >
  TwitchLib chat, Helix API, and channel-point rewards used by HeroesReplay.
  Use when changing TwitchBot, rewards, FakeTwitchClient, check twitch, or /twitch-integration.
---

# Twitch integration

## Stack

- Metapackage `TwitchLib` 3.5.x (`TwitchLib.Client` 3.3, `TwitchLib.Api` Helix 3.8).
- Code lives in `src/HeroesReplay.Core/Twitch` (`TwitchBot`, fakes) with `Predictions`, `Rewards`, `RedeemedRewards`, and `ChatMessages` subfolders; namespaces match (`HeroesReplay.Core.Twitch.Predictions`).
- Dry-run: `CaptureMethod.None` still swaps in `FakeTwitchClient` / `FakeTwitchBot`. Keep the fake implementing the current `ITwitchClient` surface.
- Credentials: `appsettings.secrets.json` (`Twitch:AccessToken`, `ClientId`, `RefreshToken`, `Account`). Fill with skill `op-service-account` / `tools/fill-secrets-from-op.ps1`. Never commit it. Predictions need `channel:manage:predictions` on that access token, and redemptions need `channel:read:redemptions` or `channel:manage:redemptions` (`check twitch` reports both).

## Helix

Channel-point methods are `*Async`: `GetCustomRewardAsync`, `CreateCustomRewardsAsync`, `UpdateCustomRewardAsync`, `DeleteCustomRewardAsync`.

Helix Predictions (Blue/Red who-wins): `twitch connect` calls `CreatePredictionAsync` when `status.json` phase is `TimerDetected`, and `EndPredictionAsync` (RESOLVED/CANCELED) from the completion fields when the session ends. Team 0 = Blue (left), team 1 = Red (right). Requires `channel:manage:predictions`. Toggle `Twitch:EnablePredictions` (true in base settings, false in `appsettings.dev.json` because the dev box shares the live channel). Window `Twitch:PredictionWindow` (base `00:02:00`, clamped 30s–1800s). `Twitch:DryRunMode` logs the prediction without calling Helix; `CaptureMethod.None` opens none. The spectator does not call Helix.

## Redemptions are EventSub

Channel-point redemptions arrive over **EventSub** (`EventSubRewardListener`, `channel.channel_points_custom_reward_redemption.add` on `wss://eventsub.wss.twitch.tv`). PubSub rewards were shut down. `ITwitchPubSub` is still registered but nothing listens on it. Do not add `ListenToRewards` / `OnRewardRedeemed` usage. The listener runs during `twitch connect` when `Twitch:EnablePubSub` or `Twitch:EnableRequests` is true (the setting name is historical).

Reward titles come from the map catalog (`Maps:Catalog` in `appsettings.json`), so run `twitch rewards *` from the repo root with `dotnet run --project src/HeroesReplay.CLI --no-launch-profile`, or from the CLI output directory.

Chat reconnects with backoff (1s doubling to 30s) on disconnect and rejoins the channel. EventSub reconnects with the same backoff and follows Twitch reconnect URLs. `Client_OnDisconnected` must not call `Connect()` in a tight loop.

Helix can only **update** channel-point rewards created with the same Client-Id as the current token. Older twitchtokengenerator rewards 403 on PATCH; their titles still match for redemptions. `twitch rewards test --title` runs the handler locally without a viewer redeem.

Chat (during `twitch connect`, chatbot enabled), matching Icy Veins observer hotkeys. The request is written to `%LOCALAPPDATA%\HeroesReplay\panel-requests.json`. The spectator process consumes it:
- `!talents` → Ctrl+1 talent panel
- `!stats` → Ctrl+2 stats panel
Each shows for `Spectate:StatsPanelShowDuration` (default 10s) with `StatsPanelCooldown` (default 2 minutes, independent per panel). Do not auto-cycle KDA/XP/stats; talents still open automatically at talent times.

## CLI

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check twitch
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch connect
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch say --message "text"
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards generate
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards submit
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards list
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards remove-unranked-draft
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch rewards test --title "Random (SL)"
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- twitch predictions test --outcome Blue
```

`twitch predictions test` creates a 30s Blue/Red Helix prediction then resolves (`Blue`/`Red`) or `cancel`. Watch it on the Twitch creator dashboard.

`twitch connect` opens the real match prediction. It watches `%LOCALAPPDATA%\HeroesReplay\status.json` and does not call into the spectator. Phase `TimerDetected` opens Blue/Red for `map`. When the session ends, the spectator writes `completedReplayId`, `completedAt`, and `completedWinnerTeam` (0 blue, 1 red, null cancels) and leaves them in place while the next replay loads. The spectator process does not call Helix.

Twitch allows one ACTIVE or LOCKED prediction per channel. When a new game opens and the channel already has one that is not this replay's (a prediction from another machine, another data directory, or a previous replay that never settled), it is cancelled first, which refunds every point, and then the new Blue/Red prediction is created. If that cancel fails, no prediction opens for this game.

After a resolved prediction, `PredictionReportWriter` writes `Data/prediction-report.html` and updates `Data/prediction-streaks.json`. Helix only returns `top_predictors` for each outcome, not every viewer. Winners increment a streak; losers reset to 0. The same prediction id is not applied twice. The end-of-match report cycle shows `match-report` first (the full Heroes Profile match page, scrolled slowly, 1 minute), then OBS scene `prediction-report` shows that file for 10 seconds, then `request-queue` for 10 seconds. `prediction-report` is skipped when the file is not there yet.

`check twitch` is Helix `GetUsers` plus `GetPredictions` when `EnablePredictions` is true, and it lists the token scopes. `twitch connect` blocks. Command map: skill `heroes-replay-cli`.
