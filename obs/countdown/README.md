# Stream countdown

Local page for the OBS **countdown** Browser Source on `waiting-screen`. No Streamerific, no iframe, no audio.

The clock starts when `waiting-screen` becomes active. Between games that is after the report scenes (`match-report` 60s, `prediction-report` 10s, `request-queue` 10s) inside the `OBS:BeforeNextReplay` hold (90s), while the next replay launches and loads.

## OBS Browser Source

URL (query string is the config):

```
countdown/index.html?m=2&s=0&autostart=1&title=NEXT%20MATCH&end=STARTING
```

Suggested source size: **720×240** (title + clock). At the old **225×100** size only the digits show.

| Query | Default | Meaning |
| --- | --- | --- |
| `m` | 2 | Minutes |
| `s` | 0 | Extra seconds |
| `title` | STREAM STARTING | Line above the clock |
| `end` | LIVE | Text when it hits zero |
| `autostart` | 1 | `0` to wait for Space |
| `color` | gold | CSS color |
| `bg` | transparent | e.g. `#111` |
| `controls` | off | `1` shows Start/Reset buttons |

Enable **Shutdown source when not visible** and **Refresh browser when scene becomes active** so each time you switch to `waiting-screen` the timer starts over.

Keys (OBS **Interact** on the source): Space start/pause, R reset, +/- 30 seconds, C toggle buttons.

Open the same file in a browser to preview.
