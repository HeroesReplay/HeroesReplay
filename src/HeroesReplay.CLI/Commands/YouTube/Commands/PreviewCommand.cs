using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Metadata.Highlights;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.YouTube.Commands;

/// <summary>
/// Prints the YouTube title, description and tags a replay would get (#412), offline: the same
/// focus timeline and match insights spectate computes, with no YouTube or Heroes Profile call.
/// </summary>
public class PreviewCommand : Command
{
    public PreviewCommand()
        : base(
            "preview",
            "Print the full match title, description lines and tags a .StormReplay would get, with its match insights: the solo laners, standouts, MVP, camps, core race, comeback, each player's closest guide build, and the hero the focus timeline follows most. Highlights (YouTube:Titles:Highlights) are on for the preview unless --configured. Mode and rank come from a cache-style file name (<id>_<mode>_<rank>_<map>_...). Offline and read-only: no YouTube, Heroes Profile, game or OBS."
        )
    {
        var replayOption = new Option<string>("--replay")
        {
            Description = "The .StormReplay file.",
            Required = true,
        };
        var configuredOption = new Option<bool>("--configured")
        {
            Description =
                "Use YouTube:Titles:Highlights as configured instead of turning highlights on.",
        };
        var insightsOption = new Option<bool>("--insights")
        {
            Description = "Also print the insights.json spectate would write.",
        };
        Options.Add(replayOption);
        Options.Add(configuredOption);
        Options.Add(insightsOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(replayOption),
                    parseResult.GetValue(configuredOption),
                    parseResult.GetValue(insightsOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string path,
        bool configured,
        bool printInsights,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        console ??= Console.Out;
        error ??= Console.Error;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error.WriteLine($"Replay was not found: {path}");
            return 1;
        }

        (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
            path,
            false,
            new ParseOptions
            {
                IgnoreErrors = true,
                AllowPTR = true,
                ShouldParseEvents = true,
                ShouldParseUnits = true,
                ShouldParseStatistics = true,
                ShouldParseMouseEvents = false,
                ShouldParseDetailedBattleLobby = false,
                ShouldParseMessageEvents = false,
            }
        );
        if (replay?.Players == null || replay.Players.Length == 0)
        {
            error.WriteLine($"The replay did not parse: {result}");
            return 1;
        }

        using ServiceProvider provider = new ServiceCollection()
            .AddReportServices(cancellationToken, typeof(ReplayFileProvider))
            .BuildHeroesReplayProvider();
        AppSettings settings = provider.GetRequiredService<AppSettings>();
        IGameData gameData = provider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        MatchInsights insights = provider
            .GetRequiredService<IMatchInsightsReader>()
            .Read(
                replay,
                gameData.Heroes,
                provider.GetRequiredService<IReplayAnalyzer>().GetPlayers(replay)
            );
        YouTubeSettings youtube = settings.YouTube ?? new YouTubeSettings();
        if (!configured)
        {
            youtube.Titles ??= new YouTubeTitleSettings();
            youtube.Titles.Highlights ??= new HighlightSettings();
            youtube.Titles.Highlights.Enabled = true;
        }

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            Loaded(path, replay),
            youtube,
            isCompleteRecording: true,
            gameData.Heroes,
            heroStats: null,
            insights
        );
        Print(entry, console);
        if (printInsights)
        {
            console.WriteLine();
            console.Write(InsightsJson.Write(insights));
        }

        return 0;
    }

    public static void Print(YouTubeEntry entry, TextWriter console)
    {
        console.WriteLine("Title: " + entry.Title);
        console.WriteLine();
        foreach (string line in entry.DescriptionLines ?? Array.Empty<string>())
        {
            console.WriteLine(line);
        }

        console.WriteLine();
        console.WriteLine("Tags: " + string.Join(", ", entry.Tags ?? Array.Empty<string>()));
    }

    /// <summary>The replay as spectate holds it, with the mode and rank a cache file name carries.</summary>
    internal static LoadedReplay Loaded(string path, Replay replay)
    {
        string[] parts = Path.GetFileNameWithoutExtension(path).Split('_');
        int? id =
            parts.Length > 0
            && int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value
            )
                ? value
                : null;
        return new LoadedReplay
        {
            ReplayId = id,
            Replay = replay,
            FileInfo = new FileInfo(path),
            HeroesProfileReplay =
                parts.Length >= 3
                    ? new HeroesProfileReplay
                    {
                        Id = id ?? 0,
                        GameType = parts[1],
                        Rank = string.Equals(
                            parts[2],
                            "Unknown",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? null
                            : parts[2],
                        GameVersion = replay.ReplayVersion,
                        GameDate = replay.Timestamp.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture
                        ),
                    }
                    : null,
        };
    }
}
