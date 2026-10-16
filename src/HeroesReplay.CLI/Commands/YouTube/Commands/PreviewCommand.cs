using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
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
        var focusOption = new Option<string>("--focus")
        {
            Description =
                "Preview the replay as the cycle's focus-hero video (#433) following this player (Name#1234): the 'Build Hero gameplay' title, the 'Hero focus:' line and the focus tags.",
        };
        Options.Add(replayOption);
        Options.Add(configuredOption);
        Options.Add(insightsOption);
        Options.Add(focusOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(replayOption),
                    parseResult.GetValue(configuredOption),
                    parseResult.GetValue(insightsOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken,
                    parseResult.GetValue(focusOption)
                )
        );
    }

    internal static async Task<int> RunAsync(
        string path,
        bool configured,
        bool printInsights,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken,
        string focus = null
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

        LoadedReplay loaded = Loaded(path, replay);
        if (!string.IsNullOrWhiteSpace(focus))
        {
            loaded.FocusPick = FocusPickFor(
                replay,
                focus,
                gameData.Heroes,
                insights,
                loaded.ReplayId ?? 0
            );
            if (loaded.FocusPick == null)
            {
                error.WriteLine($"{focus} did not play a catalog hero in this replay.");
                return 1;
            }
        }

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            loaded,
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

    /// <summary>
    /// The focus pick spectate would make for this player: the catalog hero and role, and the
    /// guide build only when the talents clearly match it. Null when the player is not in the
    /// replay or plays no catalog hero.
    /// </summary>
    internal static FocusPick FocusPickFor(
        Replay replay,
        string battleTag,
        IReadOnlyList<Hero> heroes,
        MatchInsights insights,
        int replayId
    )
    {
        int? index = PlayerPriorityRequest.PlayerIndex(
            replay,
            new RewardRequest { BattleTag = battleTag?.Trim() }
        );
        if (index is not int slot)
        {
            return null;
        }

        Player player = replay.Players[slot];
        Hero hero = PlayedHero.Find(heroes, replay, player);
        if (hero == null || string.IsNullOrWhiteSpace(hero.Role))
        {
            return null;
        }

        PlayerBuild build = insights?.Builds?.Players?.FirstOrDefault(candidate =>
            candidate.PlayerIndex == slot
        );
        bool clear = build != null && !build.Ambiguous && build.Score >= 0.85;
        return new FocusPick(
            replayId,
            replayId.ToString(CultureInfo.InvariantCulture),
            slot,
            $"{player.Name}#{player.BattleTag}",
            hero.Name,
            hero.AttributeId,
            hero.Role,
            clear ? build.Build : null
        );
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
