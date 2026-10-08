# OBS operations

How HeroesReplay installs, updates, checks, and drives OBS Studio on a machine, and what each machine owns. This is the one place for the OBS operating model. `obs/README.md` describes the packaged collection itself. The `obs-websocket-v5` skill covers the code, the `heroes-replay-cli` skill lists every command, `heroes-replay-obs` is the agent's safety checklist for both machines, and `obs-docs` is the generic OBS and obs-websocket reference.

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

  Any other collection is custom: an operator added or removed a scene or source. A custom collection is never replaced, and the output names the extra and missing names. A collection whose record says `Merged` (written by a merge, below) is managed only while its names equal that record.
- **Template changed.** A managed collection is replaced with the new template. A managed collection with no record yet is replaced once by a release (`update install-obs`, which `apply-release.ps1` runs): equal names do not mean equal filters and settings, so an older build's collection is never taken for this template. `services start` and the spectator only update its paths and save no record, so they never replace an operator's collection that a release has not seen (#218).
- **Template unchanged.** Only the asset and data paths are pointed at this install. Positions, volumes and filters that OBS saved are kept.
- **Template changed, custom collection (#307).** When the record names a template that HeroesReplay still has (this install's, `--previous`'s, or the copy in `obs\templates`), the three-way diff ([Planning an update](#planning-an-update-obs-plan-307)) decides:
  - The operator only **added** (scenes, sources, filters, settings) and nothing conflicts: the template's changes are merged in and the additions kept (`ObsCollectionMerge`, the same merge as `obs apply`). The merge must compare as the template plus exactly those additions, or nothing is written. The record then names the new template, the merged collection's own scene and source names, and `Merged: true`: that collection is never replaced with a template, and the next template change is merged again under the same rule.
  - A **conflict** (the operator and the template changed the same value), an operator **override** or **removal**, or no stored base: the collection is kept as it is, as before, and the update log lists what stopped the merge. `obs apply --backup` with OBS closed merges it while keeping overrides and removals.
  - **While OBS runs** a merge waits until HeroesReplay finds OBS closed (`services start`, `update install-obs`, or an OBS launch by the spectator). It never goes in through the live swap: OBS could save newer operator changes over a merge made from the file.
  - A managed collection (the names match, and the record has no `Merged`) is replaced as before, whatever its overrides.
- **Template store.** Every template the patcher writes from, and the replaced install's, is kept in `%LOCALAPPDATA%\HeroesReplay\obs\templates\<SHA-256>.json` (the newest 10, and every one a record names), so a merge still has its base after `app.previous` moves on. A copy whose bytes no longer match its name is ignored.
- **Backup and atomic write.** Every write copies the current file to `%LOCALAPPDATA%\HeroesReplay\obs\backups\<folder>-<file>.<UTC>.bak` (the newest 10 per file), writes a temp file beside it, and swaps it in. A failed write leaves the original. A release rollback puts the right backup back by itself (below); by hand, close OBS and run `heroesreplay obs restore <backup>` ([Backups by hand](#backups-by-hand-obs-backup-obs-restore)).
- **Release rollback (#304).** A release's `update install-obs --previous` records in `%LOCALAPPDATA%\HeroesReplay\obs\release-rollback.json` the collection file, when the install started, the backup its own write took (if any), the replaced install's template hash, and the collection's record before the install. When the health gate rolls the release back, `apply-release.ps1` runs `update restore-obs` with the failed exe, after `services stop` and before `app.previous` is copied back:
  - **Source.** The backup the install took, else the first backup of the collection from after the install started (a live swap or `services start` of the failed build). That is the collection as the restored build left it. No such backup means the release never wrote the collection: nothing is restored, the record goes back to what it was, and the log says so.
  - **OBS closed.** The backup's exact bytes are written back through `ObsFileTransaction` (the failed release's collection becomes a backup too).
  - **OBS running.** The backup goes in through the same live swap as a release (`{SceneCollectionName}-next`), so the stream and a recording stay up. When the swap cannot run (websocket down, another collection active, `OBS:LiveCollectionSwap` off), the restore waits in `obs\restore-pending.json`, `services status` shows `OBS rollback: waiting.`, and the restored build finishes it the next time it finds OBS closed or at its next replay. Any other install drops it. A build from before this existed ignores the file and replaces the collection with its own template instead.
  - **Record.** `managed-collections.json` goes back to its entry from before the install (the restored template), so the restored build keeps the file. With no entry then, it names the restored template.
  - **Never over a custom collection.** A collection whose names match neither the failed release's record, its template, nor the backup was changed by hand after the release wrote it: it is kept, `restore-obs` exits 1, and the log names the backup to copy by hand.
  - The profile (`basic.ini`) is not part of it: a release never replaces an existing profile.
- **Effective settings.** The data folder and collection name come from the install's `appsettings.json`, with the `HEROES_REPLAY_ENV` overlay and `HEROES_REPLAY_` variables applied.
- **New machine.** `tools/bootstrap-workstation.ps1` writes the collection only when the machine has none.

### Planning an update (`obs plan`, #307)

`heroesreplay obs plan [--install <dir>] [--previous <dir>] [--output json]` shows what an update would change, and changes nothing. It reads files only (no websocket), so it is safe while OBS runs: the live collection, `managed-collections.json`, and `restore-pending.json` are read, and the update's own decision (`ObsCollectionPatcher`, run as `update install-obs` runs it) is made on copies in a temp folder that is deleted. To preview a staged release, run its exe with `--install <staged folder> --previous C:\heroesreplay\app`.

- **Structured diff.** The live collection, `--install`'s `obs/Default.json`, and the base (the template the collection was last written from: this install's, `--previous`'s, or the copy in `obs\templates`, found by the SHA-256 in the record; `base` is `install`, `previous`, `stored`, or `none`) are compared after the path rewrite, property by property, for every source, filter, and scene item (a source placed more than once in a scene has `occurrence` 2, 3, ...):

  | Kind | Means | A merge |
  | --- | --- | --- |
  | `managedChange`, `managedAddition`, `managedRemoval` | The template moved; the live value is still the old template's | takes the template's |
  | `operatorOverride`, `operatorAddition`, `operatorRemoval` | The operator changed, added, or removed it; the template did not | keeps it (a removed managed source is never re-added) |
  | `conflict` | Both changed it | refuses |
  | `unattributed` | It differs and there is no base | keeps it |

  A source that exists on one side only is one line; its filters and scene items go with it.
- **Not compared.** Ids OBS assigns (`uuid`, scene item ids), hotkeys, private settings, plug-in version stamps, the order of sources, filters, and items, global audio, transitions, and the values the spectator sets per replay: the info and tier text sources' text and file, the visibility of the game scene items it shows and hides, and each report browser source's url, css (the match report scroll), and height. Fractions compare at the single precision OBS saves (`0.66` and `0.6600000262260437` are equal); whole numbers, such as colors, compare exactly.
- **Update.** `update.action` is what `update install-obs` would do now: `none`, `create`, `replace` (the whole collection, with the template), `merge` (the template's changes into a custom collection where the operator only added), `update_paths`, `restore` (a waiting release rollback), or `keep` (custom or unreadable), with `deferred` and `liveSwap` when it waits for OBS (a merge is never a live swap).
- **Codes.** `obs.plan_in_sync`, `obs.plan_changes`, `obs.plan_base_unknown`, `obs.collection_custom`, `obs.collection_missing` (ok), and `obs.plan_conflict`, `obs.collection_unreadable`, `obs.template_missing` (not ok, exit 1). JSON is `schemaVersion` 1, `ok`, `code`, `message`, `base`, `update`, `pendingRollback`, `summary`, `differences`.
### Merging by hand (`obs apply`, #307)

`heroesreplay obs apply [--backup] [--install <dir>] [--previous <dir>] [--output json]` merges `--install`'s template changes into the live collection and keeps everything of the operator's: overrides, additions, and removals (a removed managed source is never re-added). It is the same three-way diff as `obs plan`, and the same merge an update makes, without the update's "additions only" rule.

- **Without `--backup`** nothing is written, and it is safe while OBS runs: it says what the merge takes and keeps (`obs.apply_ready`), or why it refuses.
- **With `--backup`** OBS must be closed (`obs.apply_obs_running`: OBS saves its collection over the file). The live collection is backed up through `ObsFileTransaction`, the merge written atomically (`obs.applied`), the template kept in `obs\templates`, and the record names this template and, when the collection keeps operator work, `Merged: true`, so no update replaces it. `obs\apply-undo.json` keeps the record from before; `obs restore` of the backup the apply names puts the collection and that record back.
- **Refused, nothing written:** `obs.apply_conflict` (both changed a value; `blocking` lists each), `obs.apply_base_unknown` (no record, or its template is neither this install's, `--previous`'s, nor stored), `obs.apply_rollback_pending` (a release rollback waits for this collection), `obs.apply_unverified` (the merged collection did not compare as the template plus the operator's work), `obs.apply_failed`, and `obs.collection_missing`, `obs.collection_unreadable`, `obs.template_missing`. `obs.apply_in_sync` has nothing to write.
- JSON is `schemaVersion` 1, `ok`, `code`, `message`, `base`, `written`, `backup`, `taken`, `kept`, `differences`, `blocking`. Exit 1 whenever `ok` is false.

### Backups by hand (`obs backup`, `obs restore`)

- `heroesreplay obs backup [--list] [--output json]` copies the live collection into `%LOCALAPPDATA%\HeroesReplay\obs\backups` (the same folder and names every write uses, newest 10 kept) and lists the backups newest first with time, size, and SHA-256. It only reads the collection, so it is safe while OBS runs. `--list` only lists.
- `heroesreplay obs restore <backup> [--output json]` writes a backup (a path, or a file name from the list) back over the live collection byte for byte, through `ObsFileTransaction`: the collection it replaces becomes a backup, so a restore can be undone. It clears a release rollback waiting in `restore-pending.json` (the latest write wins) and leaves `managed-collections.json` as it is, except for the backup `obs apply` took: then the record goes back to what it was before the apply (`apply-undo.json`). It is refused while OBS runs (`obs.restore_obs_running`: OBS saves its collection over the file), for a backup of another file such as a profile (`obs.backup_other_file`), and for a file that is not a scene collection (`obs.backup_invalid`).

## Checking OBS

- `heroesreplay obs inspect [--output json]` reads live OBS without changing it: versions, profile and collection, canvas, output size and FPS, output mode, recording format and encoders, the stream and recording bitrates and rate control, the record directory, scenes, global audio, stream and record status, stats, the stream service (never the key), and the arm.
- `heroesreplay obs validate [--output json]` checks the loaded collection and profile against `obs/Default.json` and this install's settings. Findings have stable codes; any `error` makes it exit 1. Run it after changing OBS, the profile, or the collection, and before a release.
- `heroesreplay obs plan [--output json]` shows, from the files, what an update would change in the collection and who changed each difference ([Planning an update](#planning-an-update-obs-plan-307)).
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
| `obs.scene_item_misplaced` | warning | A driven item or the game capture is not where `obs/Default.json` puts it (position, anchor, scale, or bounding box). Right-click it > Transform > Edit Transform, or let a release replace a managed collection. See [the policy](#machine-profile-policy). |
| `obs.bitrate_low` | warning | The stream or recording video bitrate is below the floor for the output size and FPS. Settings > Output: raise the Video Bitrate. |
| `obs.stream_service_unexpected` | warning | Settings > Stream: Twitch. |
| `obs.filter_stale` | warning | An old `Scroll` filter on the match report source. Right-click the source > Filters, and disable or remove it. With loop off it moves the page out of its frame and the scene looks blank (transparent). |
| `obs.filter_missing`, `obs.path_stale`, `obs.collection_custom` | warning | Let `services start` update a managed collection while OBS is closed, or fix the source by hand. |

The full list is in the `heroes-replay-cli` skill (`obs_validate`).

## Machine profile policy

Each machine owns its OBS profile. Start one with the OBS Auto-Configuration Wizard on that machine; don't copy another machine's `basic.ini`. HeroesReplay validates only these constraints:

- **Canvas 1920x1080.** The scenes, the full-canvas report pages, and the match report's one-canvas scroll are laid out for it. The output (scaled) resolution and the encoder are the machine's choice.
- **30 FPS or more** (60 recommended).
- **Scene items where `obs/Default.json` puts them.** HeroesReplay shows and hides the info, tier, rank, and report sources but never moves them. `obs validate` compares each contract item, and each game capture in a contract scene, with the template: the position and anchor, then the scale, or the bounding box when the item has one. Up to 2 canvas pixels or 0.01 of scale is still in place. A moved item is `obs.scene_item_misplaced`, a warning: an operator may move one on purpose. The template is the truth, for example `current-replay` anchored bottom-left (alignment 9) at (5, 1060) since #276.
- **Video bitrate at or above the floor.** Below it the game's motion breaks into blocks on stream and in the uploads. The floor depends on the output (scaled) resolution and FPS. It is three quarters of Twitch's guidance (6000, 4500, and 3000 kbps), rounded down to a multiple of 500:

  | Output | Floor |
  | --- | --- |
  | 1080p above 30 FPS | 4500 kbps |
  | 1080p at 30 FPS, or 720p above 30 FPS | 3000 kbps |
  | Anything lower | 2000 kbps |

  A bitrate below the floor is `obs.bitrate_low`, a warning that never blocks a stream or a recording.
  - Simple output: the bitrate is `VBitrate`, at CBR. A recording at `Stream` quality shares that encoder and is reported once, as the stream.
  - Advanced output: the bitrates live in `streamEncoder.json` and `recordEncoder.json`, which obs-websocket cannot read. Only a custom (FFmpeg) recording's `FFVBitrate` is checked; the other bitrates are reported as not known, and no finding is raised.
  - The floor is a proposal for the owner to confirm (#309). It is `ObsBitratePolicy`.
- **Recording container: MP4 family.** MP4, Hybrid MP4 (OBS 30.2 and later, which survives a crash better) or fragmented MP4. The YouTube uploader, the pentakill clips and retention only find `*.mp4` in `Data\Contexts`. MKV would need a remux step in all three first; that is a later slice of #130 (D4/D5).
- **Encoder.** Whatever the machine's GPU does well: QSV on Intel, NVENC on NVIDIA, x264 as the fallback. It is reported, not validated.
- **Recording path.** The spectator sets the recording folder for each replay with `SetRecordDirectory` before every `StartRecord`, so the profile's own path does not matter. `obs inspect` reports it (`GetRecordDirectory`, `recordDirectory`); between replays it is the last replay's context folder. It is not validated: no idle value is wrong.
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
