# Heroes Replay

The Heroes of the Storm Automated Spectator

## Future goals of the project

- Develop better state detection for `loading`, `playing`, `paused`, `ended` states.
- Improve the existing spectate logic
- Add ping events to focus hero criteria, e.g select heroes who are pinged of danger or ping for assistance.
- Add Twitch chat integration for team win betting
- Host a 24/7 stream similar to [SaltyTeemo](https://www.twitch.tv/saltyteemo)

## About

Heroes Replay is a project that automates spectating `.StormReplay` files. It uses [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) to parse the replay file and then using that information, it can decide which heroes and panels to select. By sending input commands
to the game.

## Features

### Settings

Various settings can be found in `appsettings.json`.

### Commands

An easy to use cli with built in help and information on how to use `heroesreplay.exe`.

```ps
heroesreplay.exe spectate hotsapi
heroesreplay.exe spectate directory
heroesreplay.exe spectate file
```

### Loading

- Load an individual replay file or provide a directory to many replay files.
- Download replay files from the HotsApi database by providing AWS credentials (S3 downloads are paid per request)
- Launches the game from Battlenet and waits for the map loading screen to determine when the game load was successful.

### State

- Ensures the game is launched and validates the required game version matches the launched process version before moving onto the next step
- Waits for the loading screen before determining the next step
- Uses native Windows calls to get a screenshot of the game (timer & end screen) to determine current state: start, running, paused, end.
- Detects the end of the game by checking for the MVP and awards screen and matching any awards given to players from the replay file exist on the end screen.

### Spectating

There is currently a priority order to who is focused by the spectator, the list below is the current order of priority.

1. Focuses on heroes that perform penta, quad, triple & multi kills.
2. Focuses on heroes that will die or kill an enemy hero.
3. Focuses on heroes doing team or map objectives. (gems and doubloons for example)
4. Focuses on heroes that are close to enemies.
5. Focuses on heroes doing camps and bosses.
6. Focuses on heroes destroying structures.
7. Focuses on heroes that are roaming.

### Information Panels

- Selects the talent tree panel when a team has received new talents.
- Selects the objective panel when an objective has been taken.
- Selects the KDA panel when a hero dies.
- Selects all panels by cycling from left to right panels throughout the game:
  - Talents  
  - DeathDamageRole
  - ActionsPerMinute
  - Experience
  - TimeDeadDeathsSelfSustain
  - CarriedObjectives
  - KillsDeathsAssists
  - CrowdControlEnemyHeroes

## Dependencies

- [.NET Core 3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1) a cross-platform version of .NET for building websites, services, and console apps.
- [Microsoft.Extensions](https://github.com/dotnet/extensions) for dependency injection, logging, and configuration.
- [Polly](https://github.com/App-vNext/Polly) for resilience and transient-fault-handling.
- [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) for parsing Heroes of the Storm  `StormReplay` files.
- [Microsoft.Windows.SDK.Contracts](https://www.nuget.org/packages/Microsoft.Windows.SDK.Contracts) for the WinRT `Windows.Media.Ocr`.
- [System.CommandLine](https://github.com/dotnet/command-line-api) for command line features.
- [xUnit](https://github.com/xunit/xunit) for unit testing.
- [FluentAssertions](https://github.com/fluentassertions/fluentassertions) for natural and, most importantly, extremely readable tests.
- [HotsApi](http://hotsapi.net/) an open Heroes of the Storm replay database where everyone can download replays.
- [NSwag.MSBuild](https://github.com/RicoSuter/NSwag/wiki/NSwag.MSBuild) to generate the api client for accessing HotsApi.
- [AWSSDK.S3](https://aws.amazon.com/sdk-for-net/) to download the `.StormReplay` files hosted in the HotsApi AWS S3 storage.
- [Ali Obs Interface](https://github.com/Ahli/Galaxy-Observer-UI) a custom `.StormInterface` for Heroes of the Storm.
- [PInvoke.NET](https://github.com/dotnet/pinvoke/) for Windows native calls (BitBlt for game window capture) & sending input to the game window.
- [OBS Web Sockets .NET](https://github.com/BarRaider/obs-websocket-dotnet) for automating and triggering scenes in OBS Studio

## Building the application

### From the command line

```powershell
git clone http://github.com/HeroesReplay/HeroesReplay.git
cd ./HeroesReplay/src/HeroesReplay/
dotnet build HeroesReplay.sln
```
## Running the application

There are a few ways to run the application explained below. You can use the `--help` option to find out more for each command.

### From the bin output folder

```powershell
cd ./src/HeroesReplay.CLI/bin/Debug/netcoreapp3.1/
heroesreplay.exe --help
```

### From the project folder

```powershell
cd ./src/HeroesReplay.CLI/
dotnet run heroesreplay --help
```


## Debugging from Visual Studio 2019 or Visual Studio Code

You can hit `Ctrl+F5` to launch the project from the IDE.  
Arguments are set in the `launch.json` and `launchSettings.json` files.

## Contributing

1. Fork it!
2. Create your feature branch: `git checkout -b my-new-feature`
3. Commit your changes: `git commit -am 'Add some feature'`
4. Push to the branch: `git push origin my-new-feature`
5. Submit a pull request :D

## License

The MIT License (MIT)

Copyright (c) 2020 Patrick Magee

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
