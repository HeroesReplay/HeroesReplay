---
name: ffmpeg
description: >
  ffmpeg and ffprobe 9.0.2 CLI on ASA-SERVER: probe a recording, cut a time range
  out of a 1920x1080 match, and concatenate those clips.
  Use when cutting pentakill clips from OBS match recordings, or /ffmpeg.
---

# ffmpeg

Installed on ASA-SERVER as the current stable release, **9.0.2** (Gyan full build, `C:\ffmpeg\bin`, on the user PATH). Confirm with `ffmpeg -version`. A line that does not start with `ffmpeg version 9.0.2` is the wrong binary. Do not install a winget or chocolatey build over it.

Official pages, read these before inventing flags:

- [ffmpeg](https://ffmpeg.org/ffmpeg.html) and [ffprobe](https://ffmpeg.org/ffprobe.html)
- [Seeking](https://trac.ffmpeg.org/wiki/Seeking)
- [Concatenate](https://trac.ffmpeg.org/wiki/Concatenate)

`ffprobe` first. Times in `clips.json` are seconds in the match file, not HUD time.

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

After either command, `ffprobe` the output and check duration, width, and height. Width and height match the source, 1920x1080 for an OBS match recording. Delete nothing from `Data\Contexts` until that check passes. The dry-run uploader deletes the match mp4 once `youtube-dry-run.json` exists.
