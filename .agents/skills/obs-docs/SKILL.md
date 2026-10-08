---
name: obs-docs
description: >
  Generic OBS Studio and obs-websocket 5 reference: where the authoritative docs are, the wire
  protocol (Hello, Identify, auth, requests, batches, events, status and close codes), profile and
  scene collection concepts, output modes and recording containers, and the v4 to v5 request map.
  Use when looking up an obs-websocket request, field, or code, or an OBS concept. Not for
  HeroesReplay's rules (skill heroes-replay-obs) or its client code (skill obs-websocket-v5).
---

# OBS and obs-websocket 5 reference

Generic facts about OBS Studio and its websocket. Nothing here is HeroesReplay policy. For what HeroesReplay may do to OBS on which machine, load `heroes-replay-obs`. For HeroesReplay's websocket client (`ObsController`, the read-only session, the validator), load `obs-websocket-v5`.

## Authoritative sources

Check a request name or field against these before writing code. Do not invent one: an unknown request type fails with status 204.

| What | Where |
| --- | --- |
| obs-websocket 5 protocol (every request, event, field, enum) | `https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md` |
| OBS Studio developer docs (libobs, frontend API, scripting, plugins) | `https://docs.obsproject.com/` |
| OBS release notes (when a feature or format arrived) | `https://github.com/obsproject/obs-studio/releases` |
| The .NET client HeroesReplay uses (`obs-websocket-dotnet` 5.x) | `https://github.com/BarRaider/obs-websocket-dotnet` |

The running server is the final word: `GetVersion` returns `obsVersion`, `obsWebSocketVersion`, `rpcVersion`, and `availableRequests`, the list of requests this OBS accepts.

## Versions

- obs-websocket 5 ships inside OBS Studio from 28.0. Earlier OBS needed the separate 4.x plugin, which spoke a different protocol on port 4444.
- The 5.x line adds requests in minor versions. Compare `availableRequests`, not the version string, when a request may be new. For example, `SetRecordDirectory` arrived in 5.3.0 (OBS 30.0).
- Enable the server in Tools > WebSocket Server Settings. The default port is 4455. Authentication is on by default, with a generated password.

## Wire protocol

Every message is a JSON text frame `{"op": <opcode>, "d": {...}}`.

| op | Name | Direction | Purpose |
| --- | --- | --- | --- |
| 0 | Hello | server to client | First message: `obsWebSocketVersion`, `rpcVersion`, and `authentication` (`challenge`, `salt`) when a password is set |
| 1 | Identify | client to server | `rpcVersion`, `authentication` string, `eventSubscriptions` bit mask |
| 2 | Identified | server to client | The session is ready. Send no request before this. |
| 3 | Reidentify | client to server | Change `eventSubscriptions` on a live session |
| 5 | Event | server to client | `eventType`, `eventIntent`, `eventData` |
| 6 | Request | client to server | `requestType`, `requestId`, `requestData` |
| 7 | RequestResponse | server to client | `requestType`, `requestId`, `requestStatus` (`result`, `code`, `comment`), `responseData` |
| 8 | RequestBatch | client to server | `requests[]`, `haltOnFailure`, `executionType` |
| 9 | RequestBatchResponse | server to client | `results[]` in order |

**Authentication.** Compute `secret = base64(sha256(password + salt))`, then `auth = base64(sha256(secret + challenge))`, and send `auth` in Identify. A wrong password closes the socket with close code 4009 (AuthenticationFailed). A request sent before Identified closes it with 4007 (NotIdentified).

**Status codes** (`requestStatus.code`). These are the ones a client most often meets. The protocol document has the full enum.

| Code | Name | Usual cause |
| --- | --- | --- |
| 100 | Success | |
| 204 | UnknownRequestType | A misspelled or newer request. Check `availableRequests`. |
| 300 | MissingRequestField | A required `requestData` field is absent. |
| 400 | InvalidRequestField | A field has the wrong value. |
| 500 | OutputRunning | Starting an output that already runs. |
| 501 | OutputNotRunning | Stopping an output that is not running. |
| 600 | ResourceNotFound | No scene, input, filter, or scene item by that name or id. |
| 601 | ResourceAlreadyExists | Creating a scene or input whose name is taken. |

**Events.** Subscribe through the Identify bit mask. High-volume events such as `InputVolumeMeters` are not in the default mask and must be requested explicitly.

## OBS concepts a client meets

- **Profile.** `%APPDATA%\obs-studio\basic\profiles\<name>\basic.ini` holds output settings: stream and record encoders, bitrates, the output mode, the recording format and path, and the stream service in `service.json` beside it.
  - Read a value with `GetProfileParameter` (`parameterCategory`, `parameterName`).
  - Write a value with `SetProfileParameter`.
  - `GetProfileList` returns `currentProfileName`.
- **Output mode.** `[Output] Mode` is `Simple` or `Advanced`, and the mode decides which section OBS reads:
  - **Simple** reads `[SimpleOutput]`, for example `RecFormat2`, `RecQuality` and `VBitrate`.
  - **Advanced** reads `[AdvOut]`. Its encoder settings live in `streamEncoder.json` and `recordEncoder.json`, which the websocket cannot read.
  - A value in the other section is ignored. For example, `[AdvOut] RecFormat2=hybrid_mp4` does nothing while `Mode=Simple`.
- **Scene collection.** `%APPDATA%\obs-studio\basic\scenes\<name>.json` holds every scene and source. `GetSceneCollectionList` returns `currentSceneCollectionName`. OBS reads the list of collection files only when it starts, so a file copied in later is not offered until the next start.
- **Global audio devices.** These are Desktop Audio 1 and 2 and Mic/Aux 1 to 4. They are top-level keys in the collection, not sources in a scene. `GetSpecialInputs` names them (`desktop1`, `desktop2`, `mic1` to `mic4`; null when disabled), and `GetInputMute` reads each one's mute.
- **Inputs, scenes, and scene items.**
  - An input is a source with a kind, such as `browser_source`, `image_source`, `ffmpeg_source` or `game_capture`.
  - A scene item places an input or a nested scene inside one scene. It has a numeric id that is unique only within that scene.
  - Find the id with `GetSceneItemId`, then show or hide the item with `SetSceneItemEnabled`.
  - Moving an item is a transform (`GetSceneItemTransform` / `SetSceneItemTransform`), not an input setting.
- **Filters** belong to a source (`GetSourceFilterList`, `SetSourceFilterEnabled`). Each has a kind, such as `scroll_filter` or `color_filter_v2`.
- **Outputs.** OBS has four: stream, record, replay buffer, and virtual camera. The first two are controlled with `StartStream` / `StopStream` / `GetStreamStatus` and `StartRecord` / `StopRecord` / `GetRecordStatus`. `GetRecordDirectory` and `SetRecordDirectory` choose where the next recording goes.
- **Stream service.** `GetStreamServiceSettings` returns the service type (`rtmp_common` for a named service such as Twitch, `rtmp_custom` otherwise) and its settings. Those settings **include the stream key**. Treat the whole response as a secret.
- **Screenshots.** `GetSourceScreenshot` returns a base64 image of any source or scene without changing the program scene.

## Recording containers

`RecFormat2` values and what OBS documents about them:

| Value | Container | Note |
| --- | --- | --- |
| `mp4` | MPEG-4 | The index is written when the recording stops. A recording cut off by a crash or a kill may not open without repair. |
| `hybrid_mp4` | Hybrid MP4 (OBS 30.2 and later) | Released as an MP4 that survives a crash. |
| `fragmented_mp4` | Fragmented MP4 | Written in fragments as it records. Some players and editors handle it less well. |
| `mkv` | Matroska | Survives a crash. Many uploaders and editors want MP4, so it is remuxed afterwards (File > Remux Recordings, or ffmpeg). |
| `mov`, `flv`, `ts`, `m3u8`, `fragmented_mov`, `hybrid_mov` | Others | |

Treat crash behavior as something to measure on the machine that records. HeroesReplay's test of it is #310.

## v4 to v5 request map

| obs-websocket 4 | obs-websocket 5 |
| --- | --- |
| `SetCurrentScene` | `SetCurrentProgramScene` |
| `GetSourcesList` | `GetInputList` |
| `GetSourceSettings` / `SetSourceSettings` | `GetInputSettings` / `SetInputSettings` |
| `SetSourceRender` | `GetSceneItemId` + `SetSceneItemEnabled` |
| `StartRecording` / `StopRecording` | `StartRecord` / `StopRecord` (+ `GetRecordStatus`) |
| `SetRecordingFolder` | `SetRecordDirectory` |
| `GetStreamingStatus` | `GetStreamStatus` |
| `TakeSourceScreenshot` | `GetSourceScreenshot` / `SaveSourceScreenshot` |

## Third-party OBS skills

The MIT `damionrashford/media-os` OBS skills were evaluated for #313 and are not vendored:
- Its `obs-docs` skill is a page fetcher that runs `uv run scripts/obsdocs.py`. On 2026-10-08 neither `uv` nor Python was installed on ASA-SERVER or the stream PC; `python` there is only the Microsoft Store alias.
- Its `obs-websocket` skill is a general remote control built around `wsctl.py`, including starting and stopping streams. That is the opposite of the read-only rule for agents on the live box.

`zeke/obs-skill` has no license and stays out. This skill is first-party and points to the upstream docs instead of copying them.
