# Service split

Design for [#14](https://github.com/HeroesReplay/HeroesReplay/issues/14). In progress on `issue-14-service-split`. The CLI orchestrates processes. It does not host long-running loops in its own process.

## What one process does today

`Program` only builds `CommandLineService` and parses args. Each long-running command then builds a service provider and blocks inside `heroesreplay.exe`:

| Entry | What it hosts |
| --- | --- |
| `spectate file` / `spectate heroesprofile` | `AddSpectateServices` then `Engine.RunAsync`. Heroes Profile mode plays the local cache only. |
| `twitch connect` | `AddTwitchServices`: chat, PubSub, and a status-file prediction watcher. No game, OCR, or OBS graph. |
| `heroesprofile download` | List and download into `Data\Standard` and `Data\Requests`. No spectate loop. |
| `youtube uploader` | `AddYouTubeServices` then `YouTubeUploader.ListenAsync` |
| `mcp` | Already a second process (stdio). Leave it that way |

`Engine.RunAsync` loads game data, then `Task.WhenAll` of two loops in that process:

- Spectator: `IReplayProvider.TryLoadNextReplayAsync` then `GameManager.LaunchAndSpectate`
- Connectivity watchdog (writes `status.json`, optional OBS)

`TwitchBot` is not started by `Engine`. Chat, rewards, and predictions belong to `twitch connect`.

`GameManager` configures the Storm client, launches Heroes of the Storm, sends keys through `GameWindowInput`, runs `Spectator` (WinRT `OcrEngine` / BitBlt), opens one OBS websocket session per replay, then kills the game. It does not call Helix. When the session ends it writes `completedReplayId`, `completedAt`, and `completedWinnerTeam` on `status.json` for the Twitch process. `heroesprofile download` lists and saves replays. `spectate heroesprofile` only plays files already on disk (`ReplayCacheProvider`). `ReplayFileProvider` plays a local queue once and does not loop the API.

`youtube uploader` is not inside `Engine`, but it is still an in-process service of the CLI. It watches `Data\Contexts` for `*.mp4` plus the YouTube entry json.

## Process boundaries

Shared state stays on disk. No message bus in v1.

| Process | Owns | Must not own |
| --- | --- | --- |
| CLI | Short commands: `services start` / `services stop` / `services status`, plus today's one-shot `check`, `client`, `calculators`, reward admin, `mcp` | `Engine.RunAsync`, `TwitchBot.InitializeAsync`, `YouTubeUploader.ListenAsync`, the Heroes Profile poll loop |
| Spectator (Windows only) | HotS process, window input, WinRT OCR / BitBlt, focus loop, per-replay OBS session, connectivity watchdog, `%LOCALAPPDATA%\HeroesReplay\status.json` (including match completion fields) | Twitch sockets, Helix predictions, Heroes Profile list/download, YouTube upload |
| Twitch | Chat, PubSub, reward handlers writing `Data\requests.json`, predictions read from `status.json` / context | Game HWND, OCR, OBS, replay parse |
| Heroes Profile downloader | List and download into `Data\Standard`; fulfill requests into `Data\Requests` | Spectating, Twitch, YouTube |
| YouTube uploader | Existing watcher: context `*.mp4` + entry json, OAuth upload | Spectating, Twitch, download |

The spectator pieces stay one process. They share the cached HWND, the capture surface, and the one OBS connection per replay (`BeginSession` / `EndSession`). Splitting input, OCR, and OBS across processes would pass window handles around for no gain.

Twitch, the downloader, and YouTube do not need the game. They can be separate Windows processes now and containers later, with `C:\heroesreplay\Data` (and secret files, not printed tokens) mounted. The game, `GameWindowInput`, and WinRT OCR cannot leave the Windows game box. OBS stays with the spectator because the session is per replay.

`CaptureMethod.None` keeps `FakeTwitchBot` / `StubController` for local runs. That stub is not a second deployable service.

## Contracts already on disk

- `%LOCALAPPDATA%\HeroesReplay\status.json` — phase, timer, map, replay id, core death, and when the session ends `completedReplayId`, `completedAt`, and `completedWinnerTeam`. Twitch predictions and `status` read this. They do not call into the spectator.
- `Data\requests.json` (and the failed file) — Twitch enqueues; the downloader fulfills; the spectator only sees local `.StormReplay` files. Both processes lock the files with a named mutex.
- `%LOCALAPPDATA%\HeroesReplay\panel-requests.json` — `!talents` and `!stats` from `twitch connect`. The spectator consumes the pending panel and sends the hotkey.
- `Data\Standard` and `Data\Requests` — replay cache. After the split, `spectate heroesprofile` becomes "play the cache" (same shape as `spectate file`).
- `Data\Contexts\<id>\` — recording, end screenshot, YouTube entry. The uploader already keys off these files.

## Order of work

1. This document only.
2. Done on this branch: `Engine` no longer starts `TwitchBot`. `twitch connect` uses `AddTwitchServices` and does not build the game/OCR graph.
3. Done on this branch: `heroesprofile download` lists and downloads. `spectate heroesprofile` uses `ReplayCacheProvider` and only plays files already in `Data\Standard` and `Data\Requests`. Existing files are seeded into `Data\spectated-ids.txt` so the cache is not replayed from the beginning.
4. Done on this branch: Blue/Red predictions run in `twitch connect`. The spectator does not call Helix. `twitch connect` opens a prediction when `status.json` phase is `TimerDetected`, and settles it from `completedReplayId` / `completedAt` / `completedWinnerTeam` (0 blue, 1 red, null cancels). Those completion fields are written when the spectate session ends and are not cleared when the next replay loads.
5. Done on this branch: `heroesreplay services start` launches four processes (`spectate heroesprofile`, `twitch connect`, `heroesprofile download`, `youtube uploader`) and records their pids in `%LOCALAPPDATA%\HeroesReplay\services.json`. Each process is detached. Stdout and stderr go to `%LOCALAPPDATA%\HeroesReplay\logs\`. `services stop` writes `%LOCALAPPDATA%\HeroesReplay\services.stop`. Spectate, `twitch connect`, `heroesprofile download`, and `youtube uploader` cancel on that file. The spectator then runs its normal shutdown, which closes Heroes of the Storm. Processes still alive after 20 seconds are killed. `services status` reports the pid list plus `status.json`. Start does not turn on Twitch ingest, and it clears a leftover stop file before launching.
6. Done on this branch: `!talents` / `!stats` and `Data\requests.json` are shared files with a cross-process lock. Chat in `twitch connect` can show a panel in the spectator process.
7. Optional later: Windows services or containers for Twitch, the downloader, and YouTube. Not for the spectator.

Do not start Twitch ingest as part of this split. Do not merge the process cut to `master` until each process runs and stops on its own.
