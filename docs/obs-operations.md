# OBS operations

How HeroesReplay installs, updates, checks, and drives OBS Studio on a machine, and what each machine owns. This is the one place for the OBS operating model. `obs/README.md` describes the packaged collection itself. The `obs-websocket-v5` skill covers the code, and the `heroes-replay-cli` skill lists every command.

## Requirements

- **OBS Studio 30.0 or later**, with obs-websocket 5.3 or later (Tools > WebSocket Server Settings, port 4455). `SetRecordDirectory` arrived in obs-websocket 5.3.0, and it is the newest request HeroesReplay sends.
- **Minimum capability set:** `ObsValidator.RequiredRequests`, the requests the spectator sends. `heroesreplay obs validate` compares it with GetVersion `availableRequests` and reports each missing one as `obs.request_unavailable`.
- **Names:** `OBS:ProfileName` and `OBS:SceneCollectionName` (default `HeroesReplay`). Before every `StartStream` and `StartRecord`, the spectator checks that OBS has that profile and collection active (`obs.profile_mismatch`, `obs.collection_mismatch`, `obs.selection_unreadable`). A mismatch starts nothing.

## Who owns what

| State | Owner | What HeroesReplay does |
| --- | --- | --- |
| Packaged assets (`obs/` in the release: images, HTML, video) | Release | Published with each release and listed in `obs/bundle.manifest`, with each file's size and SHA-256 and the scene and source contract. See [The bundle manifest](#the-bundle-manifest). |
| Scene collection (`%APPDATA%\obs-studio\basic\scenes\<name>.json`) | Application, unless the operator customizes it | Installed from `obs/Default.json` and kept in step with each release. A custom collection is never overwritten. See [Updating the collection](#updating-the-collection). |
| Generated pages (`Data\queue.html`, `Data\prediction-report.html`) | Runtime data | Written by the roles when the queue changes or a prediction opens or resolves. `heroesreplay obs pages` renders both with the current build and reloads the browser sources that show them. |
| Profile (`%APPDATA%\obs-studio\basic\profiles\<name>\basic.ini`) | The machine | `obs/Default/basic.ini` is written only when the machine has no profile of that name. An existing profile is never replaced. |
| Encoder, bitrate, output resolution, FPS, recording path | The machine | Not set by HeroesReplay. Only the [policy](#machine-profile-policy) below is validated. |
| Global audio devices | The machine | The collection has Desktop Audio only. Mic/Aux should be Disabled (`obs.mic_enabled` is an error). |
| Stream service and key (`service.json`) | Operator secret | Never packaged, copied, logged, or returned by a tool. The tools report only the service type, the named service (`Twitch`), and whether a key is set. |
| Twitch ingest arm (`%LOCALAPPDATA%\HeroesReplay\stream-armed`) | The machine | Ingest needs this arm and `OBS:StreamingEnabled`. `heroesreplay obs arm` / `disarm` / `status`. The live box is armed. ASA-SERVER is armed only for a stream proof; its OBS streams to a developer Twitch account. |

## The bundle manifest

The release's `obs` folder carries `obs\bundle.manifest`, schema 2 JSON (`ObsCollectionBundle`):

- `schemaVersion` 2, `collection` (`Default.json`), and `collectionSha256`;
- `assets`: every packaged OBS file's `path`, `size`, and `sha256`;
- `contract`: the `scenes`, the `sources` with their `kind`, and the scene `items` that `ObsContract` names, from the packaged `appsettings.json` and prod overlay.

In the repository the same file is the plain list of asset paths the build publishes. `tools/package-release.ps1` copies it into the publish folder and runs the published `heroesreplay obs bundle --install <publish> --write`, so the hashes are of the bytes in the zip. `--write` refuses a source checkout.

| Where | What happens on a mismatch |
| --- | --- |
| `tools/verify-release.ps1` (CI and `release.yml`) | Checks every size and SHA-256, `collectionSha256`, and the contract against the packaged `Default.json`, in PowerShell and with the packaged `obs bundle`. A tampered or truncated zip fails the check. |
| `update install-obs` (run by `apply-release.ps1`) | Checks the install's `obs` folder before it writes anything. A mismatch is `obs.bundle_invalid`: exit 1, and neither the collection nor the profile is written. |
| `obs validate`, `obs_validate`, the spectator's preflight | A changed or missing file, or a contract name `Default.json` lacks, is `obs.bundle_invalid` (error), one finding per file. |
| `heroesreplay obs bundle [--install dir] [--output json]` | The same check without OBS. Exit 1 on `obs.bundle_invalid`. |

Backward compatible for one release: the plain list (a source checkout) is checked for presence only. A folder with no manifest (a release packaged before schema 2) installs with a warning note, and `obs validate` reports `obs.bundle_unverified` (warning).

## Launching OBS

HeroesReplay starts OBS only when the spectator needs the websocket and `obs64` is not running (`ObsCoordinator`, `ObsLaunchDecision`). Nothing else launches it: `services start`, `update install-obs`, `apply-release.ps1`, and the logon task only check for it.

- **Arguments:** `--profile "<OBS:ProfileName>" --collection "<OBS:SceneCollectionName>"`, nothing else. OBS 32 has no flag that skips its crash dialog (`--disable-shutdown-check` does not exist in 32.2.2). `--disable-updater` and `--disable-missing-files-check` are not passed: neither dialog stops the websocket, and `obs validate` reports missing assets.
- **Crash sentinel:** OBS 32 writes `%APPDATA%\obs-studio\.sentinel\run_<uuid>` when it starts and deletes it on a clean exit. A `run_*` file left by a crash, a power loss, or a kill makes the next start wait on the "OBS Studio Crash Detected" dialog for a person, and the websocket does not start (seen on ASA-SERVER with OBS 32.2.2). Right before HeroesReplay launches OBS, and only when no `obs64` process runs, `ObsCrashSentinel` deletes every `run_*` file there and logs each one (Information, file name and when it was written). A running OBS's sentinel is never touched. Portable OBS installs are not handled. An OBS the operator starts some other way (a startup shortcut) still shows the dialog after an unclean exit.
- **Startup grace:** after HeroesReplay starts OBS, the identify is retried (10 s attempts, 2 s apart) until `OBS:StartupIdentifyTimeout` (default 60 s) or until that OBS exits. An OBS that was already running gets one attempt.
- **No OBS is not a lost replay:** when `BeginSession` still cannot identify OBS, spectate logs a warning and plays the replay without OBS (clock and hero selection go on). That session has no scene change, recording, or report scenes, and the next replay tries OBS again.

## Updating the collection

`services start`, `heroesreplay update install-obs` (run by `apply-release.ps1`), and an OBS launch by the spectator all go through `ObsCollectionPatcher`.

- **Never over the active collection.** While OBS runs, `services start`, `update install-obs`, and the stream reconcile only defer (`Deferred`).
- **Live swap, between replays.** When the deferred change is a new template, the spectator's next `BeginSession` puts it in without stopping the stream or a recording (`OBS:LiveCollectionSwap`, default true; `ObsLiveCollectionSwap`). It writes the new layout to the spare collection `{SceneCollectionName}-next`, switches OBS to it (OBS saves the old one as it leaves), rewrites the main file, which is no longer active, with a backup and the template's record, and switches back to the main name and the scene that was on program. It needs `{SceneCollectionName}` active; another active collection is left alone. OBS lists collection files only when it starts, so the first swap on a machine creates the spare through OBS, which shows an empty collection until the switch back. If the switch back fails, OBS stays on the spare, which has the new layout under the wrong name, and an error is logged. Proven on ASA-SERVER (OBS 32.2.2): stream and recording stayed connected through every switch.
- **Closed OBS.** With the swap off, or when it cannot run, the change happens the next time HeroesReplay finds OBS closed. A release installed while OBS streams is not lost.
- **Managed or custom.** `%LOCALAPPDATA%\HeroesReplay\obs\managed-collections.json` records, per live collection file, the template it was last written from (SHA-256) and its scene and source names. The collection is managed when its names equal one of:
  - that record
  - this install's template
  - in a release, the replaced install's template (`--previous`)

  Any other collection is custom: an operator added or removed a scene or source. A custom collection is never overwritten, and the output names the extra and missing names.
- **Template changed.** A managed collection is replaced with the new template. A managed collection with no record yet is replaced once by a release (`update install-obs`, which `apply-release.ps1` runs): equal names do not mean equal filters and settings, so an older build's collection is never taken for this template. `services start` and the spectator only update its paths and save no record, so they never replace an operator's collection that a release has not seen (#218).
- **Template unchanged.** Only the asset and data paths are pointed at this install. Positions, volumes and filters that OBS saved are kept.
- **Backup and atomic write.** Every write copies the current file to `%LOCALAPPDATA%\HeroesReplay\obs\backups\<folder>-<file>.<UTC>.bak` (the newest 10 per file), writes a temp file beside it, and swaps it in. A failed write leaves the original. To roll back, close OBS and copy a backup over the collection.
- **Effective settings.** The data folder and collection name come from the install's `appsettings.json`, with the `HEROES_REPLAY_ENV` overlay and `HEROES_REPLAY_` variables applied.
- **New machine.** `tools/bootstrap-workstation.ps1` writes the collection only when the machine has none.

## Checking OBS

- `heroesreplay obs inspect [--output json]` reads live OBS without changing it: versions, profile and collection, canvas, output size and FPS, output mode, recording format and encoders, scenes, global audio, stream and record status, stats, the stream service (never the key), and the arm.
- `heroesreplay obs validate [--output json]` checks the loaded collection and profile against `obs/Default.json` and this install's settings. Findings have stable codes; any `error` makes it exit 1. Run it after changing OBS, the profile, or the collection, and before a release.
- The MCP server (`heroesreplay mcp`; `.mcp.json` at the repo root and in the release zip) offers the same reads as `obs_inspect`, `obs_validate`, and `obs_screenshot`. Every request is a Get, so it can't change OBS. Fixes go through guarded CLI commands. obs-mcp, which has unrestricted tools and returns the stream key, is dev-only and is never in the repo, the release, or the live box.
- **Preflight.** Before the spectator's first `StartStream` of a process, it validates over its own connection. Only `obs.request_unavailable` and `obs.stream_key_missing` stop the stream (`obsStreamBlockedBy` in `status.json`). Every other finding is logged once.

| Code | Severity | Fix |
| --- | --- | --- |
| `obs.canvas_mismatch` | error | Settings > Video > Base (Canvas) Resolution 1920x1080. The output resolution can differ. |
| `obs.recording_format` | error when `OBS:RecordingEnabled` | Settings > Output > Recording Format: MPEG-4 or Hybrid MP4. |
| `obs.stream_key_missing` | error when `OBS:StreamingEnabled` (stops the stream) | Settings > Stream: Twitch and its key. |
| `obs.mic_enabled` / `obs.mic_muted` | error / warning | Settings > Audio > Mic/Auxiliary Audio: Disabled. |
| `obs.request_unavailable` | error (stops the stream) | Update OBS to 30.0 or later. |
| `obs.bundle_invalid` | error | An install file differs from `obs/bundle.manifest` (size or SHA-256), or `Default.json` lacks a contract name. Install the release again. |
| `obs.bundle_unverified` | warning | The install has no `obs/bundle.manifest` (packaged before schema 2). The next release brings one. |
| `obs.fps_low` | warning | Settings > Video: 30 or 60 FPS. |
| `obs.stream_service_unexpected` | warning | Settings > Stream: Twitch. |
| `obs.filter_stale` | warning | An old `Scroll` filter on the match report source. Right-click the source > Filters, and disable or remove it. With loop off it moves the page out of its frame and the scene looks blank (transparent). |
| `obs.filter_missing`, `obs.path_stale`, `obs.collection_custom` | warning | Let `services start` update a managed collection while OBS is closed, or fix the source by hand. |

The full list is in the `heroes-replay-cli` skill (`obs_validate`).

## Machine profile policy

Each machine owns its OBS profile. Start one with the OBS Auto-Configuration Wizard on that machine; don't copy another machine's `basic.ini`. HeroesReplay validates only these constraints:

- **Canvas 1920x1080.** The scenes, the full-canvas report pages, and the match report's one-canvas scroll are laid out for it. The output (scaled) resolution, bitrate and encoder are the machine's choice.
- **30 FPS or more** (60 recommended).
- **Recording container: MP4 family.** MP4, Hybrid MP4 (OBS 30.2 and later, which survives a crash better) or fragmented MP4. The YouTube uploader, the pentakill clips and retention only find `*.mp4` in `Data\Contexts`. MKV would need a remux step in all three first; that is a later slice of #130 (D4/D5).
- **Encoder.** Whatever the machine's GPU does well: QSV on Intel, NVENC on NVIDIA, x264 as the fallback. It is reported, not validated.
- **Recording path.** The spectator sets the recording folder per replay with `SetRecordDirectory`, so the profile's own path doesn't matter.
- **Stream.** Service Twitch with that machine's own key, set in OBS on that machine. The live box streams to `saltysadism`; ASA-SERVER streams to a developer Twitch account, so its test streams never touch the live channel.

| Machine | Profile and collection | Streams | Records |
| --- | --- | --- | --- |
| ASA-SERVER (dev) | Its own, named by `OBS:ProfileName` and `OBS:SceneCollectionName` | Only for a stream proof, to the developer Twitch account: `OBS:StreamingEnabled` is false in dev, so a run sets `HEROES_REPLAY_OBS__StreamingEnabled=true` and the machine is armed | For tests: private `[TEST]` uploads or a dry run |
| DESKTOP-8SJEK72 (live) | Its own, named the same way | Yes: prod settings plus the arm | Yes |

## Running and stopping

- **Production.** The stack runs supervised from the logon task `HeroesReplay-live`. Create it with `heroesreplay services install-task --environment prod`; no administrator rights are needed. Release updates restart the stack through that task, and the stack always comes back supervised.
- **Spectate down for good.** When spectate uses its restart budget, the supervisor makes a live stream safe (`ServiceRestart:SpectateDownObs`). The default, `WaitingScene`, shows `OBS:WaitingSceneName`. `StopStream` stops the stream, and `None` leaves it.
- **`OBS:Enabled=false`.** Spectate sends OBS nothing: no scenes, no report scenes, and no recording, whatever `OBS:RecordingEnabled` and `OBS:RecordRequestedReplays` say. Spectate logs that once when a recording switch is on (#318).
- **Recordings end with the session.** Every session end stops the recording that spectate started, including a graceful `services stop` mid-match, with or without an OBS session. The stream is never stopped there.
- **`services stop`.** It succeeds only when OBS is closed or reports the stream inactive. While OBS runs with a websocket that doesn't answer, the stream may still be live, so the stop fails. On an install that doesn't stream, this isn't checked. After that it stops the recording that a killed or stuck spectate left running (#318). Spectate claims each recording in `%LOCALAPPDATA%\HeroesReplay\obs-recording.json` until OBS finalizes it. The stop reads `GetRecordStatus` and sends `StopRecord` only when the active recording started when the claim says. A recording that someone started later keeps running. It never sends `StopStream`. A claimed recording that OBS refuses to stop, or that can't be checked because the websocket doesn't answer, fails the stop.
