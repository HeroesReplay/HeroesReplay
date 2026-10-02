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

Do not connect, send one request, disconnect. Retries (`ResilienceRetry` pipelines from Microsoft.Extensions.Resilience) belong on **requests**, not TCP setup. HeroesReplay stops an OBS stream only when `OBS:StreamingEnabled` is true (prod only). It starts one only when that is true **and** the machine is armed: `%LOCALAPPDATA%\HeroesReplay\stream-armed` exists (`heroesreplay obs arm` / `obs disarm` / `obs status`). The arm is untracked, outside the release zip, and no setting can move it, so the overlay alone cannot go live. `ObsCoordinator.ReconcileStream` reads it each time; without it there is no websocket call, one warning, and `status.json` `obsStreamBlockedBy` is `obs.stream_not_armed`. Recording does not need the arm. Never arm ASA-SERVER.

## Profile and scene collection

Names come from `OBS:ProfileName` and `OBS:SceneCollectionName` (default `HeroesReplay`): the launch arguments (`--profile`, `--collection`), the live collection file `scenes\{name}.json`, and the profile folder `profiles\{name}`. Before every `StartStream` (before the waiting scene is selected) and every `StartRecord` (before a foreign recording is stopped), the coordinator calls `GetProfileList` → `currentProfileName` and `GetSceneCollectionList` → `currentSceneCollectionName` (obs-websocket-dotnet `GetProfileList()` and `GetCurrentSceneCollection()`). `ObsSelection.Check` compares them exactly. A mismatch or an unreadable answer fails closed with `ObsOutputFailure.SelectionMismatch` and a stable reason: `obs.profile_mismatch`, `obs.collection_mismatch`, or `obs.selection_unreadable`. The error is logged once per change, and `status.json` has `obsStreamBlockedBy` / `obsRecordBlockedBy`. `check obs` runs the same check.

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

The scene collection is application-owned: `obs/Default.json` → `%APPDATA%\obs-studio\basic\scenes\{SceneCollectionName}.json`. Asset paths in `Default.json` are relative to the `obs` folder. heroesreplay rewrites the live collection to the install directory that contains that folder (source checkout, or `C:\heroesreplay\app` after the release zip) when OBS is not running (at `services start` and at each `BeginSession`). It does not overwrite a custom collection (different source names) or the stream key. The collection has Desktop Audio only; there is no `AuxAudioDevice1` (Mic/Aux), which OBS treats as Disabled. Global audio devices live as top-level keys, not in `sources`, so a live collection that still has the old Mic/Aux is not a custom collection.

The profile is machine-owned: `obs/Default/basic.ini` is a template copied to `...\profiles\{ProfileName}\basic.ini` only when that file does not exist (bootstrap or a release update). Updates keep an existing profile, so encoders and bitrates can differ per machine. Recordings: the replay's context folder, `C:\heroesreplay\Data\Contexts\<id>`. `tools/bootstrap-workstation.ps1` copies the collection only while OBS is closed. Do not copy or commit `service.json`.

## Verify

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check obs
```

Expect Identify on `WebSocketEndpoint` (default `ws://127.0.0.1:4455`), a printed OBS/websocket version, and "OBS profile '…' and scene collection '…' are active." A wrong profile or collection fails the check with its reason code. `heroesreplay obs status` prints the arm and the expected names without connecting. Full CLI map: skill `heroes-replay-cli`.
