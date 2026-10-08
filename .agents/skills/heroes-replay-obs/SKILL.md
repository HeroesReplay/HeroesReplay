---
name: heroes-replay-obs
description: >
  HeroesReplay's OBS operations and safety rules: who owns the collection, profile, stream key and
  arm; which commands only read OBS and which change it; the stream preflight; and what an agent
  may do to OBS on ASA-SERVER (dev) versus DESKTOP-8SJEK72 (live). Use before touching OBS on
  either machine, changing obs/Default.json or the profile template, arming or disarming a
  machine, or diagnosing a stream or recording that did not start.
---

# HeroesReplay OBS operations and safety

`docs/obs-operations.md` is the operating model, and this skill does not repeat it. It covers ownership, the bundle manifest, launching OBS, collection updates, the validation codes and their fixes, and the machine profile policy. Read the section you need there. `obs/README.md` describes the packaged collection. For generic OBS and obs-websocket facts, load `obs-docs`. For HeroesReplay's websocket client code, load `obs-websocket-v5`.

## Before you touch OBS

1. **Find the machine** with `hostname`.
   - **`DESKTOP-8SJEK72`** is the live stream. Read only. Ask before stopping or restarting OBS, `heroesreplay`, or the game, and never experiment there.
   - **`ASA-SERVER`** is the dev and pre-live box. Its OBS runs the same managed `HeroesReplay` collection as the live box.
2. **Read before you change anything.** `heroesreplay obs status`, `obs inspect --output json` and `obs validate --output json` send only Get requests and never print the stream key.
3. **Change OBS only through HeroesReplay's own paths**, never by hand on a managed collection. Adding or renaming a source by hand makes the collection custom (`obs.collection_custom`). A release still merges template changes into a custom collection where the operator only added scenes, sources, filters, or settings (#307); a changed or removed managed value, or a conflict, stops template updates until someone runs `obs apply --backup` with OBS closed.
   - A template change (`obs/Default.json`) reaches a machine through `services start` while OBS is closed, the spectator's live swap at `BeginSession`, or a release's `update install-obs`.
   - Prove it on ASA-SERVER through that path, then check it with `obs validate` and `obs_screenshot`.
   - On ASA-SERVER (`OBS:StableAssets`, on in dev) the collection points at a verified copy of the build's OBS files in `%LOCALAPPDATA%\HeroesReplay\obs\assets\<bundle-hash>\`, never at a git worktree, so removing a worktree leaves no missing images (#330). An `obs.file_missing` finding for a path in a removed worktree names the fix: close OBS, then run `services start` from a current build. That is a path-only update. Prod keeps `app\obs` (`docs/obs-operations.md`).

## Which commands change OBS

| Command or tool | What it does to OBS | Where it is safe |
| --- | --- | --- |
| `obs status` | Nothing. It does not connect. | Both |
| `obs inspect`, `obs validate`, `obs bundle`, `obs plan`, `check obs` | Read only: Get requests, or no connection at all (`obs bundle` and `obs plan` read files). The stream key is never returned. `obs bundle --write` is the packaging step and writes only a publish folder's manifest. | Both |
| MCP `obs_inspect`, `obs_validate`, `obs_screenshot` | Read only (`ObsReadOnly`), one short session per call | Both. This is the production way for an agent to look. |
| `obs pages` | Writes `Data\queue.html` and `Data\prediction-report.html`, then reloads the browser sources that show them. The reload is its only OBS change. | Both. On the live box, only to fix a stale page. |
| `obs backup` | Nothing in OBS. Copies the live collection file into `%LOCALAPPDATA%\HeroesReplay\obs\backups` (`--list` only lists). | Both |
| `obs restore` | Writes a backup over the live collection, only while OBS is closed (it refuses otherwise). The backup `obs apply` took also puts its record back. | Dev. Live: only with the owner, in a scheduled downtime. |
| `obs arm` / `obs disarm` | Write or delete the machine's ingest arm, `%LOCALAPPDATA%\HeroesReplay\stream-armed`. OBS itself is not touched. | Live: never without the owner. Dev: only for a stream proof. |
| `services start` | Updates the collection's paths, replaces a managed collection with a new template, or merges a new template into a custom one where the operator only added, only while OBS is closed | Dev. Live: only in a scheduled downtime. |
| The spectator (`spectate`, `services start` roles) | Starts OBS when needed, changes scenes, sets the record directory and the recording format (`RecFormat2`, #310) before each recording, starts and stops the recording, starts the stream when allowed, live-swaps a new template between replays, and mutes every microphone input at each session start and before `StartStream` (`OBS:MuteMicrophones`, #314). When it starts, it sends `StopRecord` for a recording a dead spectate claimed and left running, as `services stop` does (#342). | Dev for proofs. Live: it is the production stack. |
| `services stop` | Never stops a stream. It checks that OBS is closed or not streaming, and fails otherwise. Then it sends `StopRecord` for a recording spectate claimed (`obs-recording.json`) and left running, when the claiming spectate is dead (pid and start time) and the recording's duration matches the claim (#318). | Dev. Live: only in a scheduled downtime. |
| `update install-obs`, `update migrate-stream-arm` | Write `%APPDATA%\obs-studio` and `%LOCALAPPDATA%\HeroesReplay` | Only from `apply-release.ps1`, never by hand on a dev box |
| obs-mcp (royshil) | Everything, including `StartStream` and `RemoveInput`. It returns the stream key. | Dev only, registered per machine. Never in the repo, the release zip, or the live box. |

## Two keys before a stream

Twitch ingest starts only when **both** keys are present:
- `OBS:StreamingEnabled` is true. That holds in prod, or in a dev run that sets `HEROES_REPLAY_OBS__StreamingEnabled=true`.
- The machine is armed with `heroesreplay obs arm`.

The arm is machine-local and untracked, and no setting can move it, so a settings overlay alone cannot go live. Recording needs neither key. `obs status` (`--output json`: `obs.ingest_ready`, `obs.stream_not_armed`, `obs.streaming_disabled`) says which key is missing.

ASA-SERVER streams only for a stream proof, and only to the developer Twitch account. Check its OBS stream service before going live: `obs inspect` shows the service type and whether a key is set, never the key.

## Preflight: what stops a stream or a recording

- **Profile and collection.** Before every `StartStream` and `StartRecord`, OBS must have `OBS:ProfileName` and `OBS:SceneCollectionName` active. Otherwise nothing starts (`obs.profile_mismatch`, `obs.collection_mismatch`, `obs.selection_unreadable` in `status.json` `obsStreamBlockedBy` / `obsRecordBlockedBy`).
- **Validation.** Before the first `StartStream` of a process, the spectator runs `obs validate` over its own connection. Only `ObsValidator.StreamBlockers` stop the stream: `obs.request_unavailable` and `obs.stream_key_missing`. Every other finding is logged once, and the stream starts. `obs.mic_enabled` does not block: the owner chose mute, not block (#314). The live box had no global mic on 2026-10-08.
- **Microphones.** At each replay's session start and right before `StartStream`, the spectator mutes every microphone input (global Mic/Aux and audio input capture sources, never Desktop Audio, media, or browser sources) with `SetInputMute`. A mute that fails is a warning and the stream goes on. `OBS:MuteMicrophones` (default true) turns it off. An automated machine should still have no microphone: `docs/obs-operations.md`, Microphones.
- **Already live.** A stream that is already active is confirmed and left alone. The preflight runs only when HeroesReplay has to start the stream.

## Reading the live box

Over `ssh streampc`, run the installed exe from `C:\SaltySadism\app`, with `HEROES_REPLAY_ENV=prod` already set for the user:

```powershell
heroesreplay obs validate --output json
heroesreplay obs inspect --output json
```

- Both are read-only.
- Over SSH, `C:\heroesreplay` is a junction to `C:\SaltySadism` that a network logon cannot traverse ("untrusted mount point"). Since #335, `obs validate` reads the junction itself without following it and checks the same file under its target, so the collection assets and data files that exist pass, and `obs.file_missing` or `obs.runtime_file_missing` there means the file is missing at the target too (the message names the path it checked). A build before #335 reports every one of them as missing; check such a path through `C:\SaltySadism\...` before believing it.
- `obs.file_unverifiable` (a warning) means this session could not check the path at all: a junction or symbolic link on the way that it can neither traverse nor read, named in the message. OBS, in the desktop session, may still load the file. Check it from the desktop session or through the link's target.
- Every other finding is real.

## Secrets and the profile

- **Stream key.** The stream key (`service.json`) is the operator's. It is never copied, packaged, logged, or returned by a tool.
- **The profile is machine-owned.** `obs/Default/basic.ini` is copied only when a machine has no profile. Changing the template does not change an existing machine. The one value HeroesReplay writes in it is the recording format, before each recording.
- **Output mode.** In Simple output mode OBS reads `[SimpleOutput]`. A value under `[AdvOut]` does nothing there (see `obs-docs`, Output mode).
- **Recording container: fragmented MP4 (#310).** The spectator sets `OBS:RecordingFormat` (default `fragmented_mp4`) in the active mode's `RecFormat2` with `SetProfileParameter` before each `StartRecord`, and reads it back. Nothing needs setting by hand on either machine: `obs.recording_not_crash_safe` (warning) clears after the next recording. A change to or from `hybrid_mp4` takes effect only after OBS restarts. Decision, evidence, and the checks still due before `master`: `docs/obs-operations.md`, Recording container.
