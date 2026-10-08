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
3. **Change OBS only through HeroesReplay's own paths**, never by hand on a managed collection. Adding or renaming a source by hand makes the collection custom, and a custom collection stops getting template updates (`obs.collection_custom`).
   - A template change (`obs/Default.json`) reaches a machine through `services start` while OBS is closed, the spectator's live swap at `BeginSession`, or a release's `update install-obs`.
   - Prove it on ASA-SERVER through that path, then check it with `obs validate` and `obs_screenshot`.

## Which commands change OBS

| Command or tool | What it does to OBS | Where it is safe |
| --- | --- | --- |
| `obs status` | Nothing. It does not connect. | Both |
| `obs inspect`, `obs validate`, `obs bundle`, `obs plan`, `check obs` | Read only: Get requests, or no connection at all (`obs bundle` and `obs plan` read files). The stream key is never returned. `obs bundle --write` is the packaging step and writes only a publish folder's manifest. | Both |
| MCP `obs_inspect`, `obs_validate`, `obs_screenshot` | Read only (`ObsReadOnly`), one short session per call | Both. This is the production way for an agent to look. |
| `obs pages` | Writes `Data\queue.html` and `Data\prediction-report.html`, then reloads the browser sources that show them. The reload is its only OBS change. | Both. On the live box, only to fix a stale page. |
| `obs backup` | Nothing in OBS. Copies the live collection file into `%LOCALAPPDATA%\HeroesReplay\obs\backups` (`--list` only lists). | Both |
| `obs restore` | Writes a backup over the live collection, only while OBS is closed (it refuses otherwise) | Dev. Live: only with the owner, in a scheduled downtime. |
| `obs arm` / `obs disarm` | Write or delete the machine's ingest arm, `%LOCALAPPDATA%\HeroesReplay\stream-armed`. OBS itself is not touched. | Live: never without the owner. Dev: only for a stream proof. |
| `services start` | Updates the collection's paths, or replaces a managed collection with a new template, only while OBS is closed | Dev. Live: only in a scheduled downtime. |
| The spectator (`spectate`, `services start` roles) | Starts OBS when needed, changes scenes, starts and stops the recording, starts the stream when allowed, and live-swaps a new template between replays | Dev for proofs. Live: it is the production stack. |
| `services stop` | Never stops a stream. It checks that OBS is closed or not streaming, and fails otherwise. | Dev. Live: only in a scheduled downtime. |
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
- **Validation.** Before the first `StartStream` of a process, the spectator runs `obs validate` over its own connection. Only `ObsValidator.StreamBlockers` stop the stream: `obs.request_unavailable` and `obs.stream_key_missing`. Every other finding is logged once, and the stream starts. Whether `obs.mic_enabled` should also block is an open owner decision (#314). The live box had no global mic on 2026-10-08.
- **Already live.** A stream that is already active is confirmed and left alone. The preflight runs only when HeroesReplay has to start the stream.

## Reading the live box

Over `ssh streampc`, run the installed exe from `C:\SaltySadism\app`, with `HEROES_REPLAY_ENV=prod` already set for the user:

```powershell
heroesreplay obs validate --output json
heroesreplay obs inspect --output json
```

- Both are read-only.
- Over SSH, `C:\heroesreplay` is a junction that a network logon cannot traverse. Every collection asset under `C:\heroesreplay\app\obs` therefore reports `obs.file_missing`, and every data file reports `obs.runtime_file_missing`, even though OBS (in the interactive session) loads them. Check such a path through `C:\SaltySadism\...` before believing it.
- Every other finding is real.

## Secrets and the profile

- **Stream key.** The stream key (`service.json`) is the operator's. It is never copied, packaged, logged, or returned by a tool.
- **The profile is machine-owned.** `obs/Default/basic.ini` is copied only when a machine has no profile. Changing the template does not change an existing machine.
- **Output mode.** In Simple output mode OBS reads `[SimpleOutput]`. A value under `[AdvOut]` does nothing there (see `obs-docs`, Output mode).
- **Recording container.** The container decision is open (#310).
