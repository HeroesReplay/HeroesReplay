# OBS collection (HeroesReplay)

Live machine copy. `{collection}` is `OBS:SceneCollectionName` and `{profile}` is `OBS:ProfileName`; both default to `HeroesReplay`:

- Collection: `%APPDATA%\obs-studio\basic\scenes\{collection}.json` ← this `Default.json`
- Profile: `%APPDATA%\obs-studio\basic\profiles\{profile}\basic.ini` ← `Default/basic.ini`, as a first-install template only

Do **not** commit `service.json` (Twitch stream key).

## Who owns what

| File | Owner | What updates do |
| --- | --- | --- |
| Scene collection (`Default.json`) | Application | `apply-release.ps1` replaces it while OBS is closed; while OBS is open it is left alone. `services start` and each replay session rewrite its asset paths to the install, and leave a collection with other source names unchanged. |
| Profile (`Default/basic.ini`) | The machine | Copied only when the machine has no profile of that name (`tools/bootstrap-workstation.ps1`, or a release update). An existing profile is kept: encoder, bitrate, resolution, recording format, and output mode stay as the operator set them. Start a new machine with the OBS Auto-Configuration Wizard. |
| Stream service (`service.json`) | Operator secret | Never packaged, copied, or logged. |
| Global audio | The machine | The collection has Desktop Audio only. Mic/Aux is not in it, which OBS treats as Disabled. Add a specific input device in OBS on the machine that needs one. |

## What this snapshot includes

- `waiting-screen` + `game-scene` at 1080p60
- Local countdown: `countdown/index.html`
- SoundCloud widget on Desktop Audio (`reroute_audio` off — Control audio via OBS cannot capture that player)
- Report scenes `match-report` (Heroes Profile match page), `prediction-report`, and `request-queue`
- No Mic/Aux device. Desktop Audio (`DesktopAudioDevice1`, default output) only
- Profile template: QSV local recording into `C:\heroesreplay\Data\Contexts` — no start-streaming

## Profile and collection names

OBS lists a profile by `[General] Name` in its `basic.ini` and a collection by the top-level `name` in its JSON. The folder and file carry the same name, so use file-safe names (letters, digits, `-`, `_`), such as `HeroesReplay-dev` and `HeroesReplay-live`. A template installed under another name gets that name written into it.

Before every `StartStream` and `StartRecord`, heroesreplay asks the websocket for the active profile (`GetProfileList` → `currentProfileName`) and collection (`GetSceneCollectionList` → `currentSceneCollectionName`). A mismatch does not start the output. The reason is `obs.profile_mismatch`, `obs.collection_mismatch`, or `obs.selection_unreadable`, in the log and in `status.json` (`obsStreamBlockedBy`, `obsRecordBlockedBy`). `heroesreplay check obs` reports the same check.

## Stream arm

Twitch ingest needs both `OBS:StreamingEnabled` (only `appsettings.prod.json`) and this machine's arm, `%LOCALAPPDATA%\HeroesReplay\stream-armed`. The arm is outside the repo and the release zip, and no setting can move it, so an environment overlay alone cannot go live. `heroesreplay obs arm` and `heroesreplay obs disarm` write and delete it; `heroesreplay obs status` prints both keys. Without the arm, the spectator logs one warning and `status.json` shows `obsStreamBlockedBy: obs.stream_not_armed`. Recording does not need the arm.

## Paths

`Default.json` names assets relative to this `obs` folder (`Ranks/bronze.png`, `countdown/index.html`). `obs/bundle.manifest` is the list the release zip publishes next to `heroesreplay.exe` (`C:\heroesreplay\app\obs` after extract). heroesreplay rewrites the live collection to that folder when OBS is not running, and it never copies `service.json` (the stream key). Generated pages stay under `C:\heroesreplay\Data` (`queue.html`, `prediction-report.html`; `heroesreplay obs pages` renders them with the current build). `pwsh -File tools/bootstrap-workstation.ps1 [-ProfileName name] [-SceneCollectionName name]` writes this collection only when the machine has none, while OBS is closed, and installs the profile template only when the machine has no profile.

## Updating the live collection

`services start`, `update install-obs` (a release), and an OBS launch by the spectator all go through `ObsCollectionPatcher`. It never writes while OBS is running: the change waits until one of them finds OBS closed, and the output says so.

- **Managed or custom.** `%LOCALAPPDATA%\HeroesReplay\obs\managed-collections.json` records, per live collection file, the template it was last written from (SHA-256) and its scene and source names. The live collection is managed when its names equal that record, this install's template, or (in a release) the replaced install's template (`--previous`). Any other collection is custom: an operator added or removed a scene or source. A custom collection is never overwritten; the output names the extra and missing scenes and sources.
- **Template changed.** A managed collection is replaced with this install's template when the template differs from the recorded one. A release with no record yet replaces it too, as every release did before the record existed.
- **Template unchanged.** Only the asset and data paths are pointed at this install. Everything else OBS saved (positions, volumes, filters) is kept.
- **Backup and atomic write.** Every write first copies the current file to `%LOCALAPPDATA%\HeroesReplay\obs\backups\scenes-<name>.json.<UTC time>.bak` (the newest 10 per file are kept), then writes a temp file beside it and swaps it in. A failed write leaves the collection as it was. To roll back, close OBS and copy a backup over `%APPDATA%\obs-studio\basic\scenes\<name>.json`.
- **Paths from the effective settings.** The data folder and the collection name come from the install's `appsettings.json` with the `HEROES_REPLAY_ENV` overlay and `HEROES_REPLAY_` variables, the same settings its roles read.
