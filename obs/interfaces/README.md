# Observer interfaces

`AhliObs 0.75.StormInterface` is the observer UI HeroesReplay was written against (`Ctrl+1`–`Ctrl+8` panel cycle).

## Install on a Windows machine

Copy the file into the HotS interfaces folder (create it if missing):

```
%USERPROFILE%\Documents\Heroes of the Storm\Interfaces\
```

In-game: **Options → Observer and Replay**, set both Observer and Replay to **AhliObs 0.75**.

The same names can be set in `Documents\Heroes of the Storm\Variables.txt`:

```
observerinterface=AhliObs 0.75
replayinterface=AhliObs 0.75
```

Restart Heroes of the Storm after copying so the dropdown picks it up.

From the CLI (preferred):

```powershell
heroesreplay client configure
heroesreplay client status
```

That also sets windowed 1080p (`displaymode=0`, 1920×1080) which capture/OCR needs. Quit the game before `configure`; HotS rewrites `Variables.txt` on exit.
