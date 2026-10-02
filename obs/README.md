# OBS collection (HeroesReplay)

Live machine copy:

- Collection: `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` ← this `Default.json`
- Profile: `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\basic.ini` ← `Default/basic.ini`

Do **not** commit `service.json` (Twitch stream key).

## What this snapshot includes

- `waiting-screen` + `game-scene` at 1080p60
- Local countdown: `countdown/index.html`
- SoundCloud widget on Desktop Audio (`reroute_audio` off — Control audio via OBS cannot capture that player)
- Report scenes `match-report` (Heroes Profile match page), `prediction-report`, and `request-queue`
- QSV local recording into `C:\heroesreplay\Data\Contexts` — no start-streaming

## Paths

`Default.json` names assets relative to this `obs` folder (`Ranks/bronze.png`, `countdown/index.html`). `obs/bundle.manifest` is the list the release zip publishes next to `heroesreplay.exe` (`C:\heroesreplay\app\obs` after extract). heroesreplay rewrites the live collection (`%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json`) to that folder when OBS is not running. It does not replace a custom collection, and it does not copy `basic.ini` or `service.json` (the stream key). Generated pages stay under `C:\heroesreplay\Data` (`queue.html`, `prediction-report.html`). `pwsh -File tools/bootstrap-workstation.ps1` copies this collection and profile only while OBS is closed.
