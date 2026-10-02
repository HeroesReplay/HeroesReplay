---
name: obs-websocket-v5
description: >
  OBS Studio 28+ obs-websocket 5 control for HeroesReplay (obs-websocket-dotnet 5.7).
  Use when changing ObsController, OBS settings, scenes, recording, check obs, or /obs-websocket-v5.
---

# OBS websocket 5

## Protocol

- OBS 28+ ships websocket **5**. Default URL `ws://127.0.0.1:4455` (not 4444).
- Package: `obs-websocket-dotnet` 5.7.x (net10).
- User enables **Tools → WebSocket Server Settings**. Password: `OBS:WebSocketPassword`.

## Lifetime

One TCP session per replay:

1. `BeginSession()` → update the installed collection paths, then `ConnectAsync` and wait until `IsIdentified` (timeout 10s).
2. Configure sources, set program scene, start/stop record, cycle report scenes.
3. `EndSession()` → `Disconnect` in `finally`.

Do not connect, send one request, disconnect. Retries (`ResilienceRetry` pipelines from Microsoft.Extensions.Resilience) belong on **requests**, not TCP setup. HeroesReplay starts or stops an OBS stream only when `OBS:StreamingEnabled` is true (prod only).

## API map (v4 name → v5)

| Old | New |
| --- | --- |
| `SetCurrentScene` | `SetCurrentProgramScene` |
| `GetSourcesList` | `GetInputList` |
| `GetSourceSettings` / `SetSourceSettings` | `GetInputSettings` / `SetInputSettings` |
| `SetSourceRender` | `GetSceneItemId` + `SetSceneItemEnabled` |
| `StartRecording` / `StopRecording` | `StartRecord` / `StopRecord` + `GetRecordStatus`. `RecordingEnabled` is false in base settings and true in dev and prod. Records when `RecordingEnabled` or (`RecordRequestedReplays` and the replay has a Twitch request that wants a recording), unless the replay is already on YouTube or `ReplayMedia` policy disallows it. |
| `SetRecordingFolder` | `SetRecordDirectory` |

Scene and source names stay in `appsettings` (`GameSceneName`, `WaitingSceneName`, `InfoSourceName`, `RankImagesSourceNames`, `ReportScenes`). `ReportScenes` cycle in order after the game: `match-report` (1 minute), `prediction-report` (10s), `request-queue` (10s). Post-game Heroes Profile is one scene, `match-report`: the full `Match/Single/[ID]` page, scrolled slowly. Do not add a scene per section. A `file:///` report scene is skipped while its file does not exist, and the whole cycle is skipped when the replay has no Heroes Profile id.

Both machines use the same OBS files from the repo (`obs/Default.json` → `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json`, `obs/Default/basic.ini` → `...\profiles\HeroesReplay\`). Asset paths in `Default.json` are relative to the `obs` folder. heroesreplay rewrites the live collection to the install directory that contains that folder (source checkout, or `C:\heroesreplay\app` after the release zip) when OBS is not running (at `services start` and at each `BeginSession`). It does not overwrite a custom collection (different source names) or the stream key. Recordings: the replay's context folder, `C:\heroesreplay\Data\Contexts\<id>`. `tools/bootstrap-workstation.ps1` copies the collection only while OBS is closed. Do not commit `service.json`.

## Verify

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check obs
```

Expect Identify on `WebSocketEndpoint` (default `ws://127.0.0.1:4455`) and a printed OBS/websocket version. Full CLI map: skill `heroes-replay-cli`.
