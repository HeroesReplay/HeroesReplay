# Heroes Replay

The Heroes of the Storm Automated Spectator

## Goals of the project

- Develop robust state detection for `loading`, `playing`, `paused`, `ended` states.
- Improve the existing spectate logic
- Add ping events to focus hero criteria, e.g select heroes who are pinged of danger or ping for assistance.
- Add Twitch chat integration for several features (voting, game controls)
- Intigration of [HotsApi](http://hotsapi.net/) for limitless replays
- Host a 24/7 stream similar to [SaltyTeemo](https://www.twitch.tv/saltyteemo)

## About

Heroes Replay is a project that automates spectating `.StormReplay` files.  
It uses [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) to parse the replay file and then using that information, it can decide which heroes and panels to select.

## Features

- Loads replay files and attemps to launch and spectate each replay.
- Focuses on heroes that will die or kill an enemy hero
- Focuses on heroes doing team or map objectives (gem and doubloon collecting for example)
- Focuses on heroes destroying structures
- Selects the talent tree panel when a team has just recieved new talents
- Selects the objective panel when an objective has been won
- Selects the Kill Death Assists panel when a hero dies
- Cycles through all panels throughout the game

## Dependencies

- [.NET Core 3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1) a cross-platform version of .NET for building websites, services, and console apps.
- [Microsoft.Extensions](https://github.com/dotnet/extensions) for dependency injection, logging, and configuration.
- [Polly](https://github.com/App-vNext/Polly) for resilience and transient-fault-handling.
- [Heroes.ReplayParser](https://github.com/barrett777/Heroes.ReplayParser) for parsing Heroes of the Storm  `StormReplay` files.
- [Microsoft.Windows.SDK.Contracts](https://www.nuget.org/packages/Microsoft.Windows.SDK.Contracts) for the WinRT `Windows.Media.Ocr`.
- [System.CommandLine](https://github.com/dotnet/command-line-api) for command line features.
- [xUnit](https://github.com/xunit/xunit) for unit testing.
- [FluentAssertions](https://github.com/fluentassertions/fluentassertions) for natural and, most importantly, extremely readable tests.

## Building the application

### From the command line

```powershell
git clone http://github.com/HeroesReplay/HeroesReplay.git
cd ./HeroesReplay/src/HeroesReplay/
dotnet build HeroesReplay.sln
```

### From Visual Studio 2019 or Visual Studio Code

`Ctrl+Shift+B` can be used to build the project.

## Running the application

There are a few ways to run the application explained below.

- If you do not provide a value for `--bnet`, it will assume the default installation path `C:\Program Files (x86)\Battle.net`
- If you do not provide a value for `--path`, it will default to `$env:USERPROFILE\Documents\Heroes of the Storm\Accounts`.

### From the bin output folder

```powershell
cd ./src/HeroesReplay.CLI/bin/Debug/netcoreapp3.1/
heroesreplay.exe [arguments]
```

### From the project folder

```powershell
cd ./src/HeroesReplay.CLI/
dotnet run heroesreplay [arguments]
```

### Spectate a replay that is currently running

- This will attempt to find an existing `HeroesOfTheStorm_x64.exe` process.
- You must ensure the replay argument you pass is the replay that is running.

```powershell
heroesreplay.exe --path="C:\path\to\your\replay.StormReplay" --launch=false
```

```
dotnet run heroesreplay --path="C:\path\to\your\replay.StormReplay" --launch=false
```

### Spectate a replay that is not currently running

- This will terminate any existing `HeroesOfTheStorm_x64.exe` process.
- This will launch the main `HeroesOfTheStorm_x64.exe` parent process, then execute the `HeroesSwitcher_x64.exe` to launch the correct `HeroesOfTheStorm_x64.exe` version that matches the version header in the replay file.

```powershell
heroesreplay.exe --path="C:\path\to\your\replay.StormReplay" --launch=false
```

```powershell
dotnet run heroesreplay --path="C:\path\to\your\replay.StormReplay" --launch=false
```

### Spectate replay files in a directory

- This behaves similar to running an individual replay file, but will attempt to load, parse and spectate each replay in the directory, one after another.

```powershell
heroesreplay.exe --path="C:\path\to\your\replays\"
```

```powershell
dotnet run heroesreplay --path="C:\path\to\your\replays\"
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
