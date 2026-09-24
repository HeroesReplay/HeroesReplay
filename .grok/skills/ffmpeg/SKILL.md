---
name: ffmpeg
description: >
  ffmpeg and ffprobe 9.0.2 CLI on ASA-SERVER: probe a recording, cut a time range,
  center-crop 16:9 to a 9:16 Short, and concatenate Shorts into a montage.
  Use when cutting OBS match recordings, making YouTube Shorts, or /ffmpeg.
---

# ffmpeg

Installed on ASA-SERVER as the current stable release, **9.0.2** (Gyan full build, `C:\ffmpeg\bin`, on the user PATH). Confirm with `ffmpeg -version`. A line that does not start with `ffmpeg version 9.0.2` is the wrong binary. Do not install a winget or chocolatey build over it.

Official pages, read these before inventing flags:

- [ffmpeg](https://ffmpeg.org/ffmpeg.html) and [ffprobe](https://ffmpeg.org/ffprobe.html)
- [Seeking](https://trac.ffmpeg.org/wiki/Seeking)
- [crop](https://ffmpeg.org/ffmpeg-filters.html#crop) and [scale](https://ffmpeg.org/ffmpeg-filters.html#scale)
- [Concatenate](https://trac.ffmpeg.org/wiki/Concatenate)

`ffprobe` first. Times in `clips.json` are seconds in the match file, not HUD time.

```powershell
ffprobe -v error -show_entries format=duration:stream=index,codec_type,codec_name,width,height,r_frame_rate -of json match.mp4
```

## Cut a Short

Put `-ss` and `-t` after `-i` so the cut is frame-accurate. A crop cannot stream-copy. Even dimensions only. Center crop of a 16:9 frame to 9:16, then scale to 1080x1920:

```powershell
ffmpeg -y -i match.mp4 -ss 120.0 -t 32.0 -vf "crop=trunc(ih*9/16/2)*2:ih,scale=1080:1920" -c:v libx264 -crf 20 -preset veryfast -c:a aac -b:a 160k -movflags +faststart short.mp4
```

`-ss` is the start in the file. `-t` is the length, not the end timestamp. Re-encode every Short with these settings so they can be joined without a second scale.

## Montage

Write a concat list of the Shorts in order, then copy the streams. This only works when every Short was encoded the same way.

```powershell
ffmpeg -y -f concat -safe 0 -i list.txt -c copy montage.mp4
```

`list.txt` lines are `file 'short-1.mp4'`.

After either command, `ffprobe` the output and check duration, width, and height. A Short is 1080x1920. Delete nothing from `Data\Contexts` until that check passes. The dry-run uploader deletes the match mp4 once `youtube-dry-run.json` exists.
