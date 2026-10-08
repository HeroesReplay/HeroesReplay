---
name: obs-websocket-v5
description: >
  OBS Studio 30.0+ obs-websocket 5.3+ control for HeroesReplay (obs-websocket-dotnet 5.7).
  Use when changing ObsController, OBS settings, scenes, recording, check obs, the read-only
  OBS MCP tools (obs_inspect, obs_validate, obs_screenshot), or /obs-websocket-v5.
---

# OBS websocket 5

The operating model (who owns what, collection updates, validation codes and fixes, the machine profile policy) is in `docs/obs-operations.md`; this skill covers the code.

## Protocol

- OBS 28+ ships websocket **5**; HeroesReplay needs **OBS 30.0+ (obs-websocket 5.3+)**, because `SetRecordDirectory` arrived in 5.3.0. The minimum capability set is `ObsValidator.RequiredRequests` (the requests the spectator sends); `obs validate` reports any the running OBS lacks as `obs.request_unavailable`. Default URL `ws://127.0.0.1:4455` (not 4444).
- Package: `obs-websocket-dotnet` 5.7.x (net10).
- User enables **Tools → WebSocket Server Settings**. Password: `OBS:WebSocketPassword`.

## Lifetime

One TCP session per replay:

1. `BeginSession()` → update the installed collection paths, then `ConnectAsync` and wait until `IsIdentified` (timeout 10s). When HeroesReplay just started OBS itself (after `ObsCrashSentinel` deleted stale `.sentinel\run_*` files, so no Crash Detected dialog), the identify is retried until `OBS:StartupIdentifyTimeout` (60 s). A `BeginSession` that still fails is a warning in `GameManager`: the replay is spectated without OBS, and `ObsController` skips scene, info, and recording calls until `EndSession` (`docs/obs-operations.md`, Launching OBS).
2. Configure sources, set program scene, start/stop record, cycle report scenes.
3. `EndSession()` → `Disconnect` in `finally`.

Do not connect, send one request, disconnect. (The read-only MCP tools below are a separate client: one short session per tool call.) Retries (`ResilienceRetry` pipelines from Microsoft.Extensions.Resilience) belong on **requests**, not TCP setup. HeroesReplay stops an OBS stream only when `OBS:StreamingEnabled` is true (prod, or a dev run that sets `HEROES_REPLAY_OBS__StreamingEnabled=true`). It starts one only when that is true **and** the machine is armed: `%LOCALAPPDATA%\HeroesReplay\stream-armed` exists (`heroesreplay obs arm` / `obs disarm` / `obs status`). The arm is untracked, outside the release zip, and no setting can move it, so the overlay alone cannot go live. `ObsCoordinator.ReconcileStream` reads it each time; without it there is no websocket call, one warning, and `status.json` `obsStreamBlockedBy` is `obs.stream_not_armed`. Recording does not need the arm. ASA-SERVER may be armed for a stream proof: its OBS streams to a developer Twitch account, not the live channel.

## Profile and scene collection

Names come from `OBS:ProfileName` and `OBS:SceneCollectionName` (default `HeroesReplay`): the launch arguments (`--profile`, `--collection`), the live collection file `scenes\{name}.json`, and the profile folder `profiles\{name}`. Before every `StartStream` (before the waiting scene is selected) and every `StartRecord` (before a foreign recording is stopped), the coordinator calls `GetProfileList` → `currentProfileName` and `GetSceneCollectionList` → `currentSceneCollectionName` (obs-websocket-dotnet `GetProfileList()` and `GetCurrentSceneCollection()`). `ObsSelection.Check` compares them exactly. A mismatch or an unreadable answer fails closed with `ObsOutputFailure.SelectionMismatch` and a stable reason: `obs.profile_mismatch`, `obs.collection_mismatch`, or `obs.selection_unreadable`. The error is logged once per change, and `status.json` has `obsStreamBlockedBy` / `obsRecordBlockedBy`. `check obs` runs the same check.

## Agent inspection (read-only MCP)

`heroesreplay mcp` has three OBS tools for agents on dev and production: `obs_inspect`, `obs_validate`, and `obs_screenshot` (output shapes and codes: skill `heroes-replay-cli`, MCP). They are the production way for an agent to look at OBS.

- **Read-only by construction.** They talk to OBS only through `IObsReadSession` (`ObsWebsocketReadSessionFactory`). `ObsReadOnly.Require` runs before every request and allows only the Get requests in `ObsReadOnly.Requests` (`GetVersion`, `GetStats`, `GetProfileList`, `GetSceneCollectionList`, `GetVideoSettings`, `GetCurrentProgramScene`, `GetSceneList`, `GetSceneItemList`, `GetInputList`, `GetInputSettings`, `GetInputMute`, `GetInputVolume`, `GetSpecialInputs`, `GetStreamStatus`, `GetRecordStatus`, `GetStreamServiceSettings`, `GetSourceScreenshot`, `GetProfileParameter`, `GetSourceFilterList`). No Set, Start, Stop, Create, Remove, or Save request can be sent. `ObsMcpToolsTests` runs all three tools against `FakeObs` and fails on any other request.
- **Own short session.** Each tool call identifies (3 s timeout), reads, and disconnects. This is per tool call, not per request, and it does not touch the spectator's one session per replay. A closed OBS returns `obs.unreachable` in about 2–3 s.
- **Stream key.** `GetStreamServiceSettings` returns the key, server, and any username or password. `ObsStreamService.Summarize` keeps only `streamServiceType` and whether `key` is non-empty; nothing else from that response leaves it.
- **Validation** reuses `ObsContract` (also used by `check obs`), `ObsSelection`, `ObsCollectionPaths.RewriteValue` / `MissingAssets` / `SourceKinds` / `SourceFilters`, and `ObsCollectionPatcher.Drift`. Mic/Aux comes from `GetSpecialInputs` (`mic1`–`mic4`): unmuted is `obs.mic_enabled` (error), muted is `obs.mic_muted` (warning). The fix for both is Settings > Audio > Global Audio Devices > Mic/Auxiliary Audio > Disabled.
- **Canvas, profile, service, filters.** The base (canvas) resolution must be `ObsContract.CanvasWidth` x `CanvasHeight` (1920x1080; `obs.canvas_mismatch`, error); the output size is the machine's. Below 30 FPS is `obs.fps_low`. `ObsProfileInfo.Read` uses `GetProfileParameter` (`Output/Mode`, then `SimpleOutput/RecFormat2` or `AdvOut/RecFormat2`, or `AdvOut/FFExtension` for a Custom Output): a format outside `mp4`, `hybrid_mp4`, `fragmented_mp4` is `obs.recording_format`, an error when `OBS:RecordingEnabled`, because the uploader, clips, and retention only find `*.mp4`. When `OBS:StreamingEnabled`, no key is `obs.stream_key_missing` (error) and a service other than `rtmp_common`/`Twitch` is `obs.stream_service_unexpected`. Each filter `obs/Default.json` gives a source must be on the live source (`GetSourceFilterList`; `obs.filter_missing`, warning). An enabled `scroll_filter` that `obs/Default.json` does not give a browser source is `obs.filter_stale` (warning): with loop off it moves the page out of its frame, and OBS draws the source transparent.
- **CLI.** `heroesreplay obs inspect` and `obs validate` (`--output text|json`) run the same tools through `ObsLiveRead`, the harness the MCP tools use. `validate` exits 1 on any error finding.
- **Preflight.** Before the spectator's first `StartStream` of a process (after the selection check), `ObsCoordinator` validates over its own connection (`ObsBorrowedReadSession`: the same Get guard, and it never disconnects). Only `ObsValidator.StreamBlockers` (`obs.request_unavailable`, `obs.stream_key_missing`) stop the stream: `ObsOutputFailure.PreflightFailed`, with the code in `status.json` `obsStreamBlockedBy`, retried on the next reconcile. Every other finding is logged once and the stream starts. A preflight that cannot run does not stop the stream.
- `obs_screenshot` returns the whole PNG as an MCP image (default width 960, at most 1920). It never changes the program scene.

Fixes stay out of MCP: `obs arm` / `obs disarm` and `obs pages` today, and later `obs plan` / `apply` (#130 workstream B).

**Generated pages.** `Data\queue.html` and `Data\prediction-report.html` are not collection assets. The roles render them when the queue changes or a prediction opens or resolves, so a new build's layout reaches OBS only then. `heroesreplay obs pages` renders both with the current build and reloads every browser source whose local file or `file://` URL is one of them (`IObsPageSession`: the read-only Gets plus `PressInputPropertiesButton` `refreshnocache`, nothing else).

**obs-mcp (royshil) is dev-only.** It registers about 120 tools with no read-only mode, including `StartStream`, `StopStream`, `SetCurrentProfile`, `RemoveInput`, and `GetStreamServiceSettings` (which returns the stream key). On ASA-SERVER it may be registered per machine for interactive tweaking (`claude mcp add obs --scope user -e OBS_WEBSOCKET_URL=ws://127.0.0.1:4455 -- cmd /c npx -y obs-mcp@latest`). It is never in the repo, the release zip, or the production machine's agent config. Its `obs-get-source-screenshot` returns only the first 100 base64 characters; use `obs_screenshot`.

## API map (v4 name → v5)

| Old | New |
| --- | --- |
| `SetCurrentScene` | `SetCurrentProgramScene` |
| `GetSourcesList` | `GetInputList` |
| `GetSourceSettings` / `SetSourceSettings` | `GetInputSettings` / `SetInputSettings` |
| `SetSourceRender` | `GetSceneItemId` + `SetSceneItemEnabled` |
| `StartRecording` / `StopRecording` | `StartRecord` / `StopRecord` + `GetRecordStatus`. `RecordingEnabled` is false in base settings and true in dev and prod. Records only while `OBS:Enabled` is true (false sends OBS nothing, recording included, #318), and then when `RecordingEnabled` or (`RecordRequestedReplays` and the replay has a Twitch request that wants a recording), unless the replay is already on YouTube or `ReplayMedia` policy disallows it. Every session end, a graceful `services stop` included, stops the recording this process owns, with or without an OBS session. Spectate claims each recording in `%LOCALAPPDATA%\HeroesReplay\obs-recording.json` (`RecordingClaimStore`) until OBS finalizes it, and `services stop` uses that claim to stop one a killed spectate left running (`OrphanRecording`; `StopRecord` only, never `StopStream`). |
| `SetRecordingFolder` | `SetRecordDirectory` |

Scene and source names stay in `appsettings` (`GameSceneName`, `WaitingSceneName`, `InfoSourceName`, `RankImagesSourceNames`, `ReportScenes`). `ReportScenes` cycle in order after the game: `match-report` (1 minute 15 seconds), `prediction-report` (10s), `request-queue` (10s). Post-game Heroes Profile is one scene, `match-report`: the full `Match/Single/[ID]` page, team sections included, in a 1920x1080 browser source. The page scrolls itself from top to bottom over the scene's `DisplayTime` (`MatchReportBrowserCss.WithScroll`); the OBS Scroll filter is not used, because the page is taller than the 8192px browser source limit. Do not add a scene per section. Each report source's CSS is rebuilt from `OBS:ReportBrowserCss` and, with `HideReportHeader`, `MatchReportBrowserCss.Header`: only the site menu and top navigation, consent dialogs, and ads are hidden. The replay id links, the footer, and the team sections show. The CSS OBS saved in the source is replaced, not appended to. A `file:///` report scene is skipped while its file does not exist, and the whole cycle is skipped when the replay has no Heroes Profile id.

The scene collection is application-owned: `obs/Default.json` → `%APPDATA%\obs-studio\basic\scenes\{SceneCollectionName}.json`. Asset paths in `Default.json` are relative to the `obs` folder. heroesreplay rewrites the live collection to the install directory that contains that folder (source checkout, or `C:\heroesreplay\app` after the release zip) when OBS is not running (at `services start` and at each `BeginSession`). It does not overwrite a custom collection (different source names) or the stream key. The collection has Desktop Audio only; there is no `AuxAudioDevice1` (Mic/Aux), which OBS treats as Disabled. Global audio devices live as top-level keys, not in `sources`, so a live collection that still has the old Mic/Aux is not a custom collection.

The profile is machine-owned: `obs/Default/basic.ini` is a template copied to `...\profiles\{ProfileName}\basic.ini` only when that file does not exist (bootstrap or a release update). Updates keep an existing profile, so encoders and bitrates can differ per machine. Recordings: the replay's context folder, `C:\heroesreplay\Data\Contexts\<id>`. `tools/bootstrap-workstation.ps1` copies the collection only while OBS is closed. Do not copy or commit `service.json`.

## Verify

```powershell
dotnet run --project src/HeroesReplay.CLI --no-launch-profile -- check obs
```

Expect Identify on `WebSocketEndpoint` (default `ws://127.0.0.1:4455`), a printed OBS/websocket version, and "OBS profile '…' and scene collection '…' are active." A wrong profile or collection fails the check with its reason code. `heroesreplay obs status` prints the arm and the expected names without connecting. Full CLI map: skill `heroes-replay-cli`.
