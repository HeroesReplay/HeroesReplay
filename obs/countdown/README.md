# Stream countdown

Local page for the OBS **countdown** Browser Source on `waiting-screen`. No Streamerific, no iframe, no audio.

The clock starts when `waiting-screen` becomes active. Between games that is after the report scenes (`match-report` 75s, `prediction-report` 10s, `request-queue` 10s), while the next replay launches and loads. The launch waits for the `OBS:BeforeNextReplay` hold (90s) or the end of that cycle, whichever is later.

## OBS Browser Source

URL (query string is the config):

```
countdown/index.html?m=2&s=0&autostart=1&title=NEXT%20MATCH&end=STARTING
```

Suggested source size: **720×240** (title + clock). At the old **225×100** size only the digits show.

The end text stays up from zero until the next replay is on screen, which can be minutes when the replay loads slowly. It is set near the title's size (`clamp(24px, 10vw, 96px)`, 72px in a 720px source), not the clock's 22vw, and the page shrinks any title or end text that is still wider than the source so it is never cut off (issue #248).

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
