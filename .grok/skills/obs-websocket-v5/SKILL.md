---
name: obs-websocket-v5
description: >
  OBS Studio 28+ obs-websocket 5 control for HeroesReplay (obs-websocket-dotnet 5.7).
  Use when changing ObsController, OBS settings, scenes, recording, check obs, the read-only
  OBS MCP tools (obs_inspect, obs_validate, obs_screenshot), or /obs-websocket-v5.
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

Do not connect, send one request, disconnect. (The read-only MCP tools below are a separate client: one short session per tool call.) Retries (`ResilienceRetry` pipelines from Microsoft.Extensions.Resilience) belong on **requests**, not TCP setup. HeroesReplay stops an OBS stream only when `OBS:StreamingEnabled` is true (prod only). It starts one only when that is true **and** the machine is armed: `%LOCALAPPDATA%\HeroesReplay\stream-armed` exists (`heroesreplay obs arm` / `obs disarm` / `obs status`). The arm is untracked, outside the release zip, and no setting can move it, so the overlay alone cannot go live. `ObsCoordinator.ReconcileStream` reads it each time; without it there is no websocket call, one warning, and `status.json` `obsStreamBlockedBy` is `obs.stream_not_armed`. Recording does not need the arm. Never arm ASA-SERVER.

## Profile and scene collection

Names come from `OBS:ProfileName` and `OBS:SceneCollectionName` (default `HeroesReplay`): the launch arguments (`--profile`, `--collection`), the live collection file `scenes\{name}.json`, and the profile folder `profiles\{name}`. Before every `StartStream` (before the waiting scene is selected) and every `StartRecord` (before a foreign recording is stopped), the coordinator calls `GetProfileList` → `currentProfileName` and `GetSceneCollectionList` → `currentSceneCollectionName` (obs-websocket-dotnet `GetProfileList()` and `GetCurrentSceneCollection()`). `ObsSelection.Check` compares them exactly. A mismatch or an unreadable answer fails closed with `ObsOutputFailure.SelectionMismatch` and a stable reason: `obs.profile_mismatch`, `obs.collection_mismatch`, or `obs.selection_unreadable`. The error is logged once per change, and `status.json` has `obsStreamBlockedBy` / `obsRecordBlockedBy`. `check obs` runs the same check.

## Agent inspection (read-only MCP)

`heroesreplay mcp` has three OBS tools for agents on dev and production: `obs_inspect`, `obs_validate`, and `obs_screenshot` (output shapes and codes: skill `heroes-replay-cli`, MCP). They are the production way for an agent to look at OBS.

- **Read-only by construction.** They talk to OBS only through `IObsReadSession` (`ObsWebsocketReadSessionFactory`). `ObsReadOnly.Require` runs before every request and allows only the Get requests in `ObsReadOnly.Requests` (`GetVersion`, `GetStats`, `GetProfileList`, `GetSceneCollectionList`, `GetVideoSettings`, `GetCurrentProgramScene`, `GetSceneList`, `GetSceneItemList`, `GetInputList`, `GetInputSettings`, `GetInputMute`, `GetInputVolume`, `GetSpecialInputs`, `GetStreamStatus`, `GetRecordStatus`, `GetStreamServiceSettings`, `GetSourceScreenshot`). No Set, Start, Stop, Create, Remove, or Save request can be sent. `ObsMcpToolsTests` runs all three tools against `FakeObs` and fails on any other request.
- **Own short session.** Each tool call identifies (3 s timeout), reads, and disconnects. This is per tool call, not per request, and it does not touch the spectator's one session per replay. A closed OBS returns `obs.unreachable` in about 2–3 s.
- **Stream key.** `GetStreamServiceSettings` returns the key, server, and any username or password. `ObsStreamService.Summarize` keeps only `streamServiceType` and whether `key` is non-empty; nothing else from that response leaves it.
- **Validation** reuses `ObsContract` (also used by `check obs`), `ObsSelection`, `ObsCollectionPaths.RewriteValue` / `MissingAssets` / `SourceKinds`, and `ObsCollectionPatcher.Drift`. Mic/Aux comes from `GetSpecialInputs` (`mic1`–`mic4`): unmuted is `obs.mic_enabled` (error), muted is `obs.mic_muted` (warning). The fix for both is Settings > Audio > Global Audio Devices > Mic/Auxiliary Audio > Disabled.
- `obs_screenshot` returns the whole PNG as an MCP image (default width 960, at most 1920). It never changes the program scene.

Fixes stay out of MCP: `obs arm` / `obs disarm` today, and later `obs plan` / `apply` (#130 workstream B).

**obs-mcp (royshil) is dev-only.** It registers about 120 tools with no read-only mode, including `StartStream`, `StopStream`, `SetCurrentProfile`, `RemoveInput`, and `GetStreamServiceSettings` (which returns the stream key). On ASA-SERVER it may be registered per machine for interactive tweaking (`claude mcp add obs --scope user -e OBS_WEBSOCKET_URL=ws://127.0.0.1:4455 -- cmd /c npx -y obs-mcp@latest`). It is never in the repo, the release zip, or the production machine's agent config. Its `obs-get-source-screenshot` returns only the first 100 base64 characters; use `obs_screenshot`.

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
