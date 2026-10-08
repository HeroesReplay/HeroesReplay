---
name: ffmpeg
description: >
  ffmpeg and ffprobe 9.0.2, pinned in src/HeroesReplay.Core/Dependencies/dependencies.json and
  installed by `heroesreplay deps install`: probe a recording, cut a time range out of a
  1920x1080 match, and concatenate those clips.
  Use when cutting pentakill clips from OBS match recordings, installing or checking ffmpeg, or /ffmpeg.
---

# ffmpeg

## The pinned build

The one definition is `src/HeroesReplay.Core/Dependencies/dependencies.json`, embedded in the exe: ffmpeg **9.0.2**, the GyanD/codexffmpeg release asset `ffmpeg-9.0.2-essentials_build.zip` (a permanent GitHub release URL), its size, and its SHA-256. The essentials build has libx264 and aac, which the clip cut uses. Nothing else names a download URL or a hash.

- `heroesreplay deps install [--dir <tools>]` downloads that zip, checks size and SHA-256, and extracts only `ffmpeg.exe` and `ffprobe.exe` into `<tools>\ffmpeg` (default `Dependencies:Directory`, `C:\heroesreplay\tools`, so `C:\heroesreplay\tools\ffmpeg`). It writes `installed.json` there and does nothing on a rerun while that build is in place. The files are staged in `<tools>\.staging` and renamed into place, so an exe is never half-written, and a failed run keeps the previous files. `apply-release.ps1` runs it with the new build after every release install; a failure only warns and never blocks or rolls back the release.
- `heroesreplay check ffmpeg` (also part of `check`) prints each tool's path, where it came from, and its `-version` line. It fails when a tool is missing, does not run, or ffmpeg cannot encode libx264. A working build that is not 9.0.2 is `[WARN]` and exits 0: clips still cut, and `deps install` puts the pinned build in a folder searched before `C:\ffmpeg\bin` and PATH.
- With clips on (`OBS:RecordingEnabled`), spectate logs one error at start when either tool cannot be found.

Clips find the tools with `FfmpegLocator`, first match wins: `Clips:FfmpegDirectory` (empty by default), then the `deps install` folder, then `C:\ffmpeg\bin`, then PATH. ASA-SERVER also has the hand-installed 9.0.2 full build at `C:\ffmpeg\bin` (on the user PATH); leave it, and do not install a winget or chocolatey build over it. A `-version` line that does not start with `ffmpeg version 9.0.2` is not the pinned build.

To move to a new build: download the new asset once, `Get-FileHash -Algorithm SHA256` it, and change `version`, `url`, `size`, and `sha256` in `dependencies.json` together. Update the version named here, in `AGENTS.md`, and in the `heroes-replay-cli` skill (`AgentDocsTests` checks them). Machines pick it up on the next release install.

Official pages, read these before inventing flags:

- [ffmpeg](https://ffmpeg.org/ffmpeg.html) and [ffprobe](https://ffmpeg.org/ffprobe.html)
- [Seeking](https://trac.ffmpeg.org/wiki/Seeking)
- [Concatenate](https://trac.ffmpeg.org/wiki/Concatenate)

The spectator already cuts pentakill clips after a recorded match (`HeroesReplay.Core.Clips.MatchClipExporter`, arguments in `FfmpegArguments.Cut`), with the tools `FfmpegLocator` finds, and writes `Data\Contexts\<id>\clips\<kind>-<hero>-<hudStart>\clip.mp4` plus an index `Data\Contexts\<id>\clips.json`.

`ffprobe` first. In `clips.json`, `fileStart` and `duration` are seconds in the match file. `hudStart` and `hudEnd` are HUD time; do not cut with them.

```powershell
ffprobe -v error -show_entries format=duration:stream=index,codec_type,codec_name,width,height,r_frame_rate -of json match.mp4
```

## Cut a clip

Put `-ss` and `-t` after `-i` so the cut is frame-accurate. Do not crop or scale. The kill can be anywhere in the 1920x1080 frame, and a center crop can remove it. The clip stays the same width and height as the match.

```powershell
ffmpeg -y -i match.mp4 -ss 120.0 -t 32.0 -c:v libx264 -crf 20 -preset veryfast -c:a aac -b:a 160k -movflags +faststart clip.mp4
```

`-ss` is the start in the file. `-t` is the length, not the end timestamp. Re-encode every clip with these settings so they can be joined without a second scale.

## Join clips from one match

Write a concat list of the clips in order, then copy the streams. This only works when every clip was encoded the same way.

```powershell
ffmpeg -y -f concat -safe 0 -i list.txt -c copy clips.mp4
```

`list.txt` lines are `file 'clip-1.mp4'`.

After either command, `ffprobe` the output and check duration, width, and height. Width and height match the source, 1920x1080 for an OBS match recording. Delete nothing from `Data\Contexts` until that check passes. `MediaRetention` deletes old match mp4s on its own (`Retention:VideoKeepDays` 3, `VideoMaxAgeDays` 7), so cut from a recent recording.
