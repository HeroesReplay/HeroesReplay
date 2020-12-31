# Heroes Replay

The Heroes of the Storm Automated Spectator. 

## Future goals of the project

- Improve the existing spectate logic.
- Twitch integration for team win betting.
- YouTube uploading of replays.

## About

Heroes Replay is a project that automates spectating `.StormReplay` files.  
It uses [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) to analyze replays for to know which heroes to focus during the game.  
The software is running on a dedicated machine streaming on the [Twitch Platform](http://twitch.tv/saltysadism)

## Features

- Running on [http://twitch.tv/saltysadism](http://twitch.tv/saltysadism).
- Integrates with Heroes Profile Website to display an end of game report such as stats, graphs & talents.
- Heroes Profile API integration for downloading replays & displaying MMR.
- A customised UI Observer Interface by [Ahli](https://github.com/Ahli/Galaxy-Observer-UI).

### Settings

Various settings can be found in `appsettings.json`.

### Commands

An easy to use cli with built in help and information on how to use `heroesreplay.exe`.

```ps
heroesreplay.exe spectate heroesprofile
heroesreplay.exe spectate directory
heroesreplay.exe spectate file
```

### Loading

- Load an individual replay file or provide a directory to many replay files.
- Download replay files from an S3 Bucket (HotsAPI or HeroesProfile) by providing AWS credentials (S3 downloads are paid per request)
- Launches the game from Battlenet and waits for the map loading screen and Timer to determine when the game load was successful.

### State

- Ensures the game is launched and validates the required game version matches the launched process version before moving onto the next step.
- Waits for the home/loading screen before determining the next step.
- Uses native Windows calls to get a screenshot of the game (timer & end screen) to determine current state: start, running, paused, end.
- Detects the end of the game by checking for the MVP and awards screen and matching any awards given to players from the replay file exist on the end screen.

### Spectating

The spectate focus target is determined by a weighted point system. Kills are worth the most and roaming is worth the least. The weightings can be found in the appsettings.

- Focuses on heroes that perform kills.
- Focuses on heroes that will die or kill an enemy hero.
- Focuses on heroes that are close to enemies.
- Focuses on heroes doing team or map objectives. (gems and doubloons)
- Focuses on heroes doing camps and bosses.
- Focuses on heroes destroying structures.
- Focuses on heroes that are roaming.

### Information Panels

- Selects the talent tree panel when a team has received new talents.
- Selects all panels by cycling from left to right panels throughout the game:
  - Talents  
  - DeathDamageRole
  - ActionsPerMinute
  - Experience
  - TimeDeadDeathsSelfSustain
  - CarriedObjectives (If the map supports it)
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
- [HeroesProfile](http://heroesprofile.com/) for downloading Replays and displaying game data.
- [NSwag.MSBuild](https://github.com/RicoSuter/NSwag/wiki/NSwag.MSBuild) to generate api clients.
- [AWSSDK.S3](https://aws.amazon.com/sdk-for-net/) to download the `.StormReplay` files hosted in AWS S3 storage.
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
