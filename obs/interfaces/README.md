# Observer interfaces

`AhliObs 0.75.StormInterface` is the observer UI HeroesReplay was written against (`Ctrl+1`–`Ctrl+8` panel cycle).

## Install on a Windows machine

Copy the file into the HotS interfaces folder (create it if missing):

```
%USERPROFILE%\Documents\Heroes of the Storm\Interfaces\
```

In-game: **Options → Observer and Replay**, set both Observer and Replay to **AhliObs 0.75**.

HotS stores the **live** observer/replay UI on the Battle.net **account** file:

`Documents\Heroes of the Storm\Accounts\<id>\Variables.txt`

The in-game dropdown name is **AhliObs 0.75** (no extension). `client configure` writes that. The game may persist `AhliObs 0.75.StormInterface`; both are treated as a match.

```
observerinterface=AhliObs 0.75
replayinterface=AhliObs 0.75
```

Root `Variables.txt` holds windowed 1080p (`displaymode` / `width` / `height`). `heroesreplay client configure` writes both.

Restart Heroes of the Storm after copying so the dropdown picks it up.

From the CLI (preferred):

```powershell
heroesreplay client configure
heroesreplay client status
```

That also sets windowed 1080p (`displaymode=0`, 1920×1080) which capture/OCR needs. Quit the game before `configure`; HotS rewrites `Variables.txt` on exit.
