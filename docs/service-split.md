# Service split

Design for [#14](https://github.com/HeroesReplay/HeroesReplay/issues/14). Not implemented. The CLI orchestrates processes. It does not host long-running loops in its own process.

## What one process does today

`Program` only builds `CommandLineService` and parses args. Each long-running command then builds a service provider and blocks inside `heroesreplay.exe`:

| Entry | What it hosts |
| --- | --- |
| `spectate file` / `spectate heroesprofile` | `AddSpectateServices` then `Engine.RunAsync` |
| `twitch connect` | Same spectate graph (OCR, game controller, OBS types) just to call `TwitchBot.InitializeAsync` and wait |
| `youtube uploader` | `AddYouTubeServices` then `YouTubeUploader.ListenAsync` |
| `mcp` | Already a second process (stdio). Leave it that way |

`Engine.RunAsync` loads game data, then `Task.WhenAll` of three loops in that process:

- Spectator: `IReplayProvider.TryLoadNextReplayAsync` then `GameManager.LaunchAndSpectate`
- Twitch: `TwitchBot.InitializeAsync` (chat and PubSub, with reconnect)
- Connectivity watchdog (writes `status.json`, optional OBS)

`GameManager` in that same process configures the Storm client, launches Heroes of the Storm, sends keys through `GameWindowInput`, runs `Spectator` (WinRT `OcrEngine` / BitBlt), opens one OBS websocket session per replay, resolves Twitch blue/red predictions, then kills the game. `HeroesProfileProvider` (the `spectate heroesprofile` provider) both downloads replays and dequeues `Data\requests.json` before that launch. `ReplayFileProvider` already plays a local queue once and does not loop the API.

`youtube uploader` is not inside `Engine`, but it is still an in-process service of the CLI. It watches `Data\Contexts` for `*.mp4` plus the YouTube entry json.

## Process boundaries

Shared state stays on disk. No message bus in v1.

| Process | Owns | Must not own |
| --- | --- | --- |
| CLI | Short commands: `start` / `stop` / `status`, plus today's one-shot `check`, `client`, `calculators`, reward admin, `mcp` | `Engine.RunAsync`, `TwitchBot.InitializeAsync`, `YouTubeUploader.ListenAsync`, the Heroes Profile poll loop |
| Spectator (Windows only) | HotS process, window input, WinRT OCR / BitBlt, focus loop, per-replay OBS session, connectivity watchdog, `%LOCALAPPDATA%\HeroesReplay\status.json` | Twitch sockets, Helix predictions, Heroes Profile list/download, YouTube upload |
| Twitch | Chat, PubSub, reward handlers writing `Data\requests.json`, predictions read from `status.json` / context | Game HWND, OCR, OBS, replay parse |
| Heroes Profile downloader | List and download into `Data\Standard`; fulfill requests into `Data\Requests` | Spectating, Twitch, YouTube |
| YouTube uploader | Existing watcher: context `*.mp4` + entry json, OAuth upload | Spectating, Twitch, download |

The spectator pieces stay one process. They share the cached HWND, the capture surface, and the one OBS connection per replay (`BeginSession` / `EndSession`). Splitting input, OCR, and OBS across processes would pass window handles around for no gain.

Twitch, the downloader, and YouTube do not need the game. They can be separate Windows processes now and containers later, with `C:\heroesreplay\Data` (and secret files, not printed tokens) mounted. The game, `GameWindowInput`, and WinRT OCR cannot leave the Windows game box. OBS stays with the spectator because the session is per replay.

`CaptureMethod.None` keeps `FakeTwitchBot` / `StubController` for local runs. That stub is not a second deployable service.

## Contracts already on disk

- `%LOCALAPPDATA%\HeroesReplay\status.json` — phase, timer, map, replay id, core death. Twitch predictions and `status` read this. They do not call into the spectator.
- `Data\requests.json` (and the failed file) — Twitch enqueues; the downloader fulfills; the spectator only sees local `.StormReplay` files.
- `Data\Standard` and `Data\Requests` — replay cache. After the split, `spectate heroesprofile` becomes "play the cache" (same shape as `spectate file`).
- `Data\Contexts\<id>\` — recording, end screenshot, YouTube entry. The uploader already keys off these files.

## Order of work

1. This document only.
2. Done on this branch: `Engine` no longer starts `TwitchBot`. `twitch connect` uses `AddTwitchServices` and does not build the game/OCR graph. Predictions still run inside the spectator until a later cut.
3. Done on this branch: `heroesprofile download` lists and downloads. `spectate heroesprofile` uses `ReplayCacheProvider` and only plays files already in `Data\Standard` and `Data\Requests`. Existing files are seeded into `Data\spectated-ids.txt` so the cache is not replayed from the beginning.
4. Point the orchestrator at the existing `youtube uploader` process. Do not fold it back into spectate.
5. Optional later: Windows services or containers for Twitch, the downloader, and YouTube. Not for the spectator.

Do not start Twitch ingest as part of this split. Do not merge the process cut to `master` until each process runs and stops on its own.
