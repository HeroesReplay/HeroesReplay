using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

/// <summary>
/// Downloads the newest replays of one map into a folder of its own, named the way the cache
/// names them, so <c>calculators units</c> and <c>calculators report</c> can study that map. It
/// never writes into the spectate queue. A download Heroes Profile refuses is skipped and the
/// next listed replay is tried (#346); the summary names each skip and why.
/// </summary>
public class SampleCommand : Command
{
    public SampleCommand()
        : base(
            "sample",
            "Download the newest Heroes Profile replays of one map into a folder for calculators units and calculators report. Never into the spectate queue."
        )
    {
        Option<string> map = new("--map")
        {
            Description = "Map name as Heroes Profile writes it, for example \"Hanamura Temple\".",
            Required = true,
        };
        Option<int> count = new("--count")
        {
            Description =
                "Replays to download, 1 to 20. Default 2. A replay whose download fails is skipped and the next listed one is tried.",
            DefaultValueFactory = _ => 2,
        };
        Option<GameType> gameType = new("--game-type")
        {
            Description = "StormLeague (default), QuickMatch, or ARAM.",
            DefaultValueFactory = _ => GameType.StormLeague,
        };
        Option<string> output = new("--output")
        {
            Description = "Folder to write into. Created when missing.",
            Required = true,
        };
        Options.Add(map);
        Options.Add(count);
        Options.Add(gameType);
        Options.Add(output);
        SetAction(
            async (parseResult, cancellationToken) =>
                await RunAsync(
                        parseResult.GetValue(map),
                        parseResult.GetValue(count),
                        parseResult.GetValue(gameType),
                        parseResult.GetValue(output),
                        cancellationToken
                    )
                    .ConfigureAwait(false)
        );
    }

    private static async Task<int> RunAsync(
        string map,
        int count,
        GameType gameType,
        string output,
        CancellationToken cancellationToken
    )
    {
        if (count < 1 || count > 20)
        {
            Console.Error.WriteLine("--count must be from 1 to 20.");
            return 1;
        }

        string folder = Path.GetFullPath(output);
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken, "heroesreplay-sample")
            .BuildHeroesReplayProvider();
        using IServiceScope scope = provider.CreateScope();
        IHeroesProfileService heroesProfile =
            scope.ServiceProvider.GetRequiredService<IHeroesProfileService>();
        AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
        if (IsQueue(folder, settings))
        {
            Console.Error.WriteLine(
                $"{folder} is a spectate queue. Sample into a folder of its own."
            );
            return 1;
        }

        IEnumerable<HeroesProfileReplay> listed = await heroesProfile
            .GetReplaysByFilters(gameType, gameRank: null, gameMap: map)
            .ConfigureAwait(false);
        // Every listed replay of the map, newest first: a skipped download moves on to the next.
        List<HeroesProfileReplay> candidates = (listed ?? Enumerable.Empty<HeroesProfileReplay>())
            .Where(replay =>
                replay.Downloadable != false
                && string.Equals(replay.Map, map, StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(replay => replay.Id)
            .ToList();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine(
                $"Heroes Profile listed no downloadable {gameType} replay of {map}."
            );
            await PrintRecentMapsAsync(heroesProfile, settings).ConfigureAwait(false);
            return 1;
        }

        Directory.CreateDirectory(folder);
        string separator = settings.StormReplay?.Seperator ?? "_";
        string extension = settings.StormReplay?.FileExtension ?? ".StormReplay";
        var sample = new SampleDownload(
            heroesProfile,
            scope.ServiceProvider.GetRequiredService<ILogger<SampleCommand>>(),
            Console.Out
        );
        SampleOutcome outcome = await sample
            .RunAsync(
                candidates,
                count,
                folder,
                replay =>
                    string.Join(
                        separator,
                        replay.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        replay.GameType,
                        replay.Rank ?? "Unknown",
                        replay.Map,
                        replay.Fingerprint,
                        extension
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);
        foreach (string line in outcome.Summary(folder))
        {
            Console.WriteLine(line);
        }

        if (outcome.ExitCode != 0)
        {
            Console.Error.WriteLine(
                $"None of the {candidates.Count} listed {gameType} replays of {map} could be downloaded."
            );
        }

        return outcome.ExitCode;
    }

    /// <summary>The maps and modes in the newest listing pages, so a misspelt or rotated-out map is plain.</summary>
    private static async Task PrintRecentMapsAsync(
        IHeroesProfileService heroesProfile,
        AppSettings settings
    )
    {
        int page = Math.Max(1, settings.HeroesProfileApi?.ApiMaxReturnedReplays ?? 1000);
        int maxId = await heroesProfile.GetMaxReplayIdAsync().ConfigureAwait(false);
        var seen = new List<HeroesProfileReplay>();
        for (int i = 1; i <= 3; i++)
        {
            IEnumerable<HeroesProfileReplay> rows = await heroesProfile
                .GetReplaysByMinId(Math.Max(1, maxId - i * page))
                .ConfigureAwait(false);
            seen.AddRange(rows ?? Enumerable.Empty<HeroesProfileReplay>());
        }

        Console.Error.WriteLine($"Maps in the newest {seen.Count} listed replays:");
        foreach (
            var group in seen.GroupBy(replay => (replay.GameType, replay.Map))
                .OrderBy(group => group.Key.GameType)
                .ThenByDescending(group => group.Count())
        )
        {
            Console.Error.WriteLine($"  {group.Key.GameType} | {group.Key.Map} | {group.Count()}");
        }
    }

    private static bool IsQueue(string folder, AppSettings settings)
    {
        string data = settings.Location?.DataDirectory;
        string[] queues =
        [
            settings.Location?.ReplaySource,
            string.IsNullOrWhiteSpace(data) || settings.HeroesProfileApi == null
                ? null
                : Path.Combine(data, settings.HeroesProfileApi.StandardCacheDirectoryName ?? ""),
            string.IsNullOrWhiteSpace(data) || settings.HeroesProfileApi == null
                ? null
                : Path.Combine(data, settings.HeroesProfileApi.RequestsCacheDirectoryName ?? ""),
        ];
        return queues.Any(queue =>
            !string.IsNullOrWhiteSpace(queue)
            && string.Equals(
                Path.GetFullPath(queue).TrimEnd('\\'),
                folder.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}
