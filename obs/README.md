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

`Default.json` names assets relative to this `obs` folder (`Ranks/bronze.png`, `countdown/index.html`). `obs/bundle.manifest` is the list the release zip publishes next to `heroesreplay.exe` (`C:\heroesreplay\app\obs` after extract). heroesreplay rewrites the live collection to that folder when OBS is not running. It does not replace a custom collection, and it never copies `service.json` (the stream key). Generated pages stay under `C:\heroesreplay\Data` (`queue.html`, `prediction-report.html`). `pwsh -File tools/bootstrap-workstation.ps1 [-ProfileName name] [-SceneCollectionName name]` copies this collection while OBS is closed and installs the profile template only when the machine has none.
