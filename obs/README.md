# OBS collection (HeroesReplay)

The packaged scene collection and its assets. How HeroesReplay installs, updates, validates, and drives it, and what each machine owns, is in [`docs/obs-operations.md`](../docs/obs-operations.md).

Live machine copy. `{collection}` is `OBS:SceneCollectionName` and `{profile}` is `OBS:ProfileName`; both default to `HeroesReplay`:

- Collection: `%APPDATA%\obs-studio\basic\scenes\{collection}.json` ← this `Default.json`
- Profile: `%APPDATA%\obs-studio\basic\profiles\{profile}\basic.ini` ← `Default/basic.ini`, as a first-install template only

Do **not** commit `service.json` (Twitch stream key).

## What this snapshot includes

- `waiting-screen` + `game-scene` on a 1920x1080 canvas at 60 FPS
- `release-version` in the bottom-right corner of `waiting-screen` only: the HeroesReplay release this install runs, which is the GitHub release tag the update downloaded (`v1.0.0-614`), or `dev <commit>` for a source build. Not the OBS version. Spectate writes `Data\heroesreplay-version.txt` when it starts and the text source reads it, so the label changes as soon as a release update restarts the stack
- Local countdown: `countdown/index.html`
- SoundCloud widget on Desktop Audio (`reroute_audio` off — Control audio via OBS cannot capture that player)
- Report scenes `match-report` (Heroes Profile match page), `prediction-report`, and `request-queue`
- No Mic/Aux device. Desktop Audio (`DesktopAudioDevice1`, default output) only
- Profile template: QSV local recording into `C:\heroesreplay\Data\Contexts` — no start-streaming. `RecFormat2=fragmented_mp4` in `[SimpleOutput]` and `[AdvOut]` (#310). On an existing machine the spectator sets the format before each recording (`OBS:RecordingFormat`)

## Names

OBS lists a profile by `[General] Name` in its `basic.ini` and a collection by the top-level `name` in its JSON. The folder and file carry the same name, so use file-safe names (letters, digits, `-`, `_`), such as `HeroesReplay-dev` and `HeroesReplay-live`. A template installed under another name gets that name written into it.

## Paths

`Default.json` names assets relative to this `obs` folder (`Ranks/bronze.png`, `countdown/index.html`). `obs/bundle.manifest` is the list the release zip publishes next to `heroesreplay.exe` (`C:\heroesreplay\app\obs` after extract). HeroesReplay points the live collection at that folder, and at `Location:DataDirectory` for the generated pages (`queue.html`, `prediction-report.html`), when OBS is not running.

## Bundle manifest

In this repository `bundle.manifest` is a plain list: one path per line, relative to this folder. The build publishes exactly those files. Add a new asset to it, or the release does not carry it.

The release zip's `obs\bundle.manifest` is schema 2 JSON, written by `tools/package-release.ps1` (`heroesreplay obs bundle --write` on the publish folder):

- `schemaVersion` (2), `collection` (`Default.json`) and `collectionSha256`;
- `assets`: each published file's `path`, `size`, and `sha256` (upper-case hex, as `Get-FileHash` prints it);
- `contract`: the `scenes`, the `sources` with their `kind`, and the scene `items` HeroesReplay drives (`ObsContract`, from the packaged `appsettings.json` and prod overlay).

`tools/verify-release.ps1` checks every hash and the contract in CI. `update install-obs` refuses a bundle that no longer matches (`obs.bundle_invalid`, nothing written), `obs validate` reports it, and `heroesreplay obs bundle` checks an install without OBS. The plain list is still read (presence only), and an install with no manifest installs and validates with a warning (`obs.bundle_unverified`). See [`docs/obs-operations.md`](../docs/obs-operations.md).
