using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

public class CoordinatesCommand : Command
{
    public CoordinatesCommand()
        : base(
            "coordinates",
            "Validate that hero coordinates still parse from a latest-client .StormReplay file."
        )
    {
        var fileOption = new Option<FileInfo>("--file")
        {
            Description =
                "Path to a .StormReplay file. Defaults to the newest replay under Documents\\Heroes of the Storm.",
        };
        fileOption.Aliases.Add("-f");
        Options.Add(fileOption);

        SetAction(parseResult =>
        {
            FileInfo file = parseResult.GetValue(fileOption) ?? FindLatestReplay();
            return ValidateCoordinates(file);
        });
    }

    internal static FileInfo FindLatestReplay()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string accountsRoot = Path.Combine(documents, "Heroes of the Storm", "Accounts");

        if (!Directory.Exists(accountsRoot))
        {
            throw new DirectoryNotFoundException(
                $"Heroes of the Storm accounts folder was not found: {accountsRoot}"
            );
        }

        FileInfo latest = new DirectoryInfo(accountsRoot)
            .EnumerateFiles("*.StormReplay", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
        {
            throw new FileNotFoundException(
                "No .StormReplay files were found under the Heroes of the Storm Documents folder."
            );
        }

        return latest;
    }

    internal static int ValidateCoordinates(FileInfo file)
    {
        if (file == null || !file.Exists)
        {
            Console.Error.WriteLine($"Replay file not found: {file?.FullName}");
            return 1;
        }

        Console.WriteLine($"Parsing {file.FullName}");
        Console.WriteLine($"Last write: {file.LastWriteTime:u}  Size: {file.Length} bytes");

        var options = new ParseOptions
        {
            AllowPTR = false,
            ShouldParseEvents = true,
            ShouldParseUnits = true,
            ShouldParseStatistics = true,
            ShouldParseMouseEvents = false,
            ShouldParseMessageEvents = false,
            ShouldParseDetailedBattleLobby = false,
            IgnoreErrors = false,
        };

        (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
            File.ReadAllBytes(file.FullName),
            options
        );

        Console.WriteLine($"Parse result: {result}");

        if (replay == null)
        {
            Console.Error.WriteLine("Parser returned no replay object.");
            return 2;
        }

        Console.WriteLine($"Map: {replay.Map}");
        Console.WriteLine($"Replay version: {replay.ReplayVersion}");
        Console.WriteLine($"Replay build: {replay.ReplayBuild}");
        Console.WriteLine($"Players: {replay.Players?.Length ?? 0}");

        int heroesWithPositions = 0;
        int totalPositions = 0;
        int samplePrinted = 0;

        foreach (Player player in replay.Players ?? Array.Empty<Player>())
        {
            IEnumerable<Unit> heroUnits = player.HeroUnits ?? Enumerable.Empty<Unit>();
            int playerPositions = heroUnits.Sum(u => u.Positions?.Count ?? 0);
            totalPositions += playerPositions;

            if (playerPositions > 0)
            {
                heroesWithPositions++;
            }

            Console.WriteLine(
                $"  {player.Name} ({player.Character}): {playerPositions} coordinate samples"
            );

            foreach (Unit unit in heroUnits)
            {
                if (unit.Positions == null)
                {
                    continue;
                }

                foreach (var position in unit.Positions)
                {
                    if (samplePrinted < 8)
                    {
                        Console.WriteLine(
                            $"    t={position.TimeSpan} point=({position.Point.X}, {position.Point.Y})"
                        );
                        samplePrinted++;
                    }
                }
            }
        }

        var heroesWithCoords = (replay.Players ?? Array.Empty<Player>())
            .Select(p => new
            {
                Player = p,
                Unit = (p.HeroUnits ?? Enumerable.Empty<Unit>()).FirstOrDefault(u =>
                    u.Positions != null && u.Positions.Count > 0
                ),
            })
            .Where(x => x.Unit != null)
            .ToList();

        if (heroesWithCoords.Count >= 2)
        {
            var first = heroesWithCoords[0];
            var second = heroesWithCoords[1];
            double distance = first
                .Unit.Positions[0]
                .Point.DistanceTo(second.Unit.Positions[0].Point);
            Console.WriteLine(
                $"Sample DistanceTo between {first.Player.Character} and {second.Player.Character}: {distance:F2}"
            );
        }

        bool parseOk =
            result == DataParser.ReplayParseResult.Success
            || result == DataParser.ReplayParseResult.UnexpectedResult;
        bool coordinatesOk = heroesWithPositions >= 2 && totalPositions > 0;

        if (!parseOk)
        {
            Console.Error.WriteLine("Replay parse did not succeed.");
            return 3;
        }

        if (!coordinatesOk)
        {
            Console.Error.WriteLine(
                "Coordinates were not parseable. Calculator logic that uses Point/DistanceTo cannot run on this replay."
            );
            return 4;
        }

        Console.WriteLine(
            $"OK: {heroesWithPositions} heroes have coordinates ({totalPositions} samples). Calculator distance checks can run."
        );

        var settings = new AppSettings
        {
            Weights = new WeightSettings
            {
                PlayerKill = 10,
                NearEnemyHero = 8,
                NearEnemyHeroOffset = 0.09f,
                NearEnemyHeroDistanceDivisor = 10000,
                Roaming = 1,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToEnemy = 20,
                MaxDistanceToEnemyKill = 15,
                MinDistanceToSpawn = 40,
                PastDeathContextTime = TimeSpan.FromSeconds(6),
                PresentDeathContextTime = TimeSpan.FromSeconds(3),
            },
        };

        var analyzer = new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            payloadsBuilder: null,
            calculators: new IFocusCalculator[]
            {
                new KillCalculator(settings),
                new NearEnemyCalculator(settings),
                new RoamingCalculator(settings),
            },
            gameData: null
        );

        var foci = analyzer.GetPlayers(replay);
        if (foci.Count == 0)
        {
            Console.Error.WriteLine("Coordinates parsed but the focus timeline was empty.");
            return 5;
        }

        var byCalculator = foci
            .Values.GroupBy(f => f.Calculator.Name)
            .ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"Focus timeline: {foci.Count} second(s) with a selected hero.");
        foreach (var pair in byCalculator.OrderByDescending(p => p.Value))
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }

        return 0;
    }
}
