# OBS collection (HeroesReplay)

Live machine copy:

- Collection: `%APPDATA%\obs-studio\basic\scenes\HeroesReplay.json` ← this `Default.json`
- Profile: `%APPDATA%\obs-studio\basic\profiles\HeroesReplay\basic.ini` ← `Default/basic.ini`

Do **not** commit `service.json` (Twitch stream key).

## What this snapshot includes

- `waiting-screen` + `game-scene` at 1080p60
- Local countdown: `countdown/index.html`
- SoundCloud widget on Desktop Audio (`reroute_audio` off — Control audio via OBS cannot capture that player)
- Heroes Profile report scenes (`summary`, `match-scores`, `talents`, `experience`, `team-1-stats`, `team-2-stats`) **hidden** until Cloudflare is sorted
- QSV local recording into `C:\heroesreplay\Data\Contexts` — no start-streaming

## Paths

Asset paths in `Default.json` are `C:/heroesreplay/HeroesReplay/obs/...`. Rewrite if the repo lives elsewhere.
