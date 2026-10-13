# Observer interfaces

`AhliObs 0.75.StormInterface` is the observer UI HeroesReplay was written against (`Ctrl+1`–`Ctrl+8` panel cycle).

## Install on a Windows machine

Copy the file into the HotS interfaces folder (create it if missing):

```
%USERPROFILE%\Documents\Heroes of the Storm\Interfaces\
```

In-game: **Options → Observer and Replay**, set both Observer and Replay to **AhliObs 0.75**.

A client signed in through Battle.net reads the observer/replay UI from the **account** file:

`Documents\Heroes of the Storm\Accounts\<id>\Variables.txt`

`client configure` writes the interface file name. The Options dropdown shows **AhliObs 0.75** and hides the extension. The dropdown label does not name the file, so the client loads the default HUD.

```
observerinterface=AhliObs 0.75.StormInterface
replayinterface=AhliObs 0.75.StormInterface
```

A client that does not sign in (an older build opened through HeroesSwitcher) reads the root `Documents\Heroes of the Storm\Variables.txt` instead. `heroesreplay client configure` writes windowed 1080p (`displaymode` / `width` / `height`) and both interface lines into the root file and every account file.

Restart Heroes of the Storm after copying so the dropdown picks it up.

From the CLI (preferred):

```powershell
heroesreplay client configure
heroesreplay client status
```

That also sets windowed 1080p (`displaymode=0`, 1920×1080) which capture/OCR needs. Quit the game before `configure`; HotS rewrites `Variables.txt` on exit.

## Blind Rating copy

`AhliObs 0.75 Blind.StormInterface` is the same interface for a Blind Rating replay (#411), with two of AhliObs's own settings changed:

| Setting (`_Config.StormLayout`) | AhliObs 0.75 | Blind |
| --- | --- | --- |
| `HeroUnitStatusNameType` (name over the health bar) | `Automatic` (player names) | `Unit` (hero names) |
| `ChatInitiallyHidden` | `false` | `true` |

Before a Blind Rating replay's client starts, spectate installs this archive **under the AhliObs file name** (`Interfaces\AhliObs 0.75.StormInterface`), and `Variables.txt` keeps naming AhliObs. When the blind replay is over it puts AhliObs back as soon as that client has closed, even when no replay follows (a stop, an empty queue, a release update). Before every other replay it checks again, so a leftover blind copy is always replaced (`client status` reports it as not AhliObs). Spectate doesn't switch `Variables.txt` to the blind file name because that doesn't work. On ASA-SERVER on 2026-10-12, both `Variables.txt` files named `AhliObs 0.75 Blind.StormInterface` before Battle.net started the client. The signed-in client still opened `AhliObs 0.75.StormInterface` and showed player names. Heroes holds the installed file open while it runs, so the copy only changes while Heroes is closed. The client is closed after every replay anyway. A client that outlived its replay with the wrong copy is restarted. `Client:BlindInterfaceFileName` names the archive in `Assets\Interfaces`; empty turns the blind HUD off.

These are the settings Ahli's Observer UI Settings Editor (from [Galaxy-Observer-UI](https://github.com/Ahli/Galaxy-Observer-UI/releases)) shows for AhliObs. Its Save rebuilds the archive with the `MPQEditor.exe` it ships. `tools/new-blind-interface.ps1` does the same from the command line. It extracts AhliObs with that `MPQEditor.exe`, changes the two `<Constant>` values, and rebuilds the archive with the editor's Heroes compression preset. Then it checks that every other file is byte-identical. Run it again whenever `AhliObs 0.75.StormInterface` changes, and commit both files:

```powershell
pwsh -File tools/new-blind-interface.ps1
```

The map loading screen is the game's own and names the players, so spectate keeps a blind replay's loading screen off the stream. The waiting or report scene stays on air until the match clock runs, then game-scene shows the match.
