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

1. `BeginSession()` → `ConnectAsync` and wait until `IsIdentified` (timeout ~10s).
2. Configure sources, set program scene, start/stop record, cycle report scenes.
3. `EndSession()` → `Disconnect` in `finally`.

Do not connect, send one request, disconnect. Polly retries belong on **requests**, not TCP setup.

## API map (v4 name → v5)

| Old | New |
| --- | --- |
| `SetCurrentScene` | `SetCurrentProgramScene` |
| `GetSourcesList` | `GetInputList` |
| `GetSourceSettings` / `SetSourceSettings` | `GetInputSettings` / `SetInputSettings` |
| `SetSourceRender` | `GetSceneItemId` + `SetSceneItemEnabled` |
| `StartRecording` / `StopRecording` | `StartRecord` / `StopRecord` + `GetRecordStatus`. Default **off**. Records when `RecordingEnabled` or (`RecordRequestedReplays` and the replay has a Twitch requestor). |
| `SetRecordingFolder` | `SetRecordDirectory` |

Scene and source names stay in `appsettings` (`GameSceneName`, `WaitingSceneName`, `InfoSourceName`, `RankImagesSourceNames`, `ReportScenes`).

Both machines use the same OBS files from the repo (`obs/Default.json` → `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json`, `obs/Default/basic.ini` → `...\profiles\HeroesReplay\`). Asset paths are `C:/heroesreplay/HeroesReplay/obs/...`. Recordings: `C:\heroesreplay\Data\Contexts`. `tools/bootstrap-workstation.ps1` copies the collection. Do not commit `service.json`.

## Verify

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check obs
```

Expect Identify on `WebSocketEndpoint` (default `ws://127.0.0.1:4455`) and a printed OBS/websocket version. Full CLI map: skill `heroes-replay-cli`.
