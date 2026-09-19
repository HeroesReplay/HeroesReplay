# Waiting music

Local Browser Source so OBS can **Control audio via OBS**. The SoundCloud widget cannot: it plays through Web Audio inside `w.soundcloud.com/player`, so the mixer fader stays dead.

This page uses a normal HTML `<audio>` element. CEF can reroute that into the `soundcloud` mixer channel.

## Setup

1. Put a loopable file here named **`track.mp3`** (or `.ogg` / `.m4a` and change `src`).
2. OBS Browser Source `soundcloud`:

```
file:///C:/heroesreplay/HeroesReplay/obs/music/index.html?autostart=1
```

Check **Control audio via OBS**. Advanced Audio: **Monitor and Output**.

| Query | Default | Meaning |
| --- | --- | --- |
| `src` | `track.mp3` | File next to this page, or a full URL to a real audio file |
| `autostart` | 1 | `0` to wait for play |
| `title` | filename | Overlay label |
| `volume` | 1 | 0–1 page volume (OBS fader still applies) |

Do not point `src` at a SoundCloud page or widget URL. That is the same broken path as before.

Do not commit large mp3s.
