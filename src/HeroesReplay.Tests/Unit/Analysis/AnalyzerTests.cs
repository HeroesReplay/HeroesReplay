using System;
using System.Linq;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AnalyzerTests : IClassFixture<ReplayFixture>
{
    private readonly ReplayFixture fixture;

    public AnalyzerTests(ReplayFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void CalculatorKills()
    {
        var settings = CreateSettings();
        var analyzer = CreateAnalyzer(settings, new KillCalculator(settings));

        var results = analyzer.GetPlayers(fixture.Replay);
        var playerKills = results
            .Values.Where(x => x.Calculator == typeof(KillCalculator))
            .GroupBy(x => x.Target.Character)
            .ToDictionary(x => x.Key, x => x);

        Assert.True(playerKills["Valla"].Count() > 7);
    }

    [Fact]
    public void KillOutweighsNearEnemyAndDeathWindowHolds()
    {
        var settings = CreateSettings();
        settings.Weights.PlayerKill = 10;
        settings.Weights.NearEnemyHero = 8;
        var analyzer = CreateAnalyzer(
            settings,
            new KillCalculator(settings),
            new NearEnemyCalculator(settings)
        );

        var results = analyzer.GetPlayers(fixture.Replay);
        Assert.NotEmpty(results);

        var killSecond = results.First(kv => kv.Value.Calculator == typeof(KillCalculator));
        Assert.True(killSecond.Value.Points >= settings.Weights.PlayerKill);

        for (int second = 1; second < 3; second++)
        {
            TimeSpan future = killSecond.Key.Add(TimeSpan.FromSeconds(second));
            if (results.TryGetValue(future, out var held))
            {
                Assert.Equal(killSecond.Value.Target, held.Target);
            }
        }
    }

    [Fact]
    public void GetSessionEnd_PrefersLastUpVotesThenScoreResult()
    {
        var settings = CreateSettings();
        settings.TrackerEvents = new TrackerEventSettings
        {
            GatesOpen = "GatesOpen",
            EndOfGameStatEvents = new[] { "EndOfGameUpVotesCollected" },
            UseScoreResultEvent = true,
        };
        var analyzer = CreateAnalyzer(settings);

        TimeSpan sessionEnd = analyzer.GetSessionEnd(fixture.Replay);

        TrackerEvent lastVote = fixture.Replay.TrackerEvents?.LastOrDefault(e =>
            e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent
            && e.Data?.dictionary != null
            && e.Data.dictionary[0].blobText == "EndOfGameUpVotesCollected"
        );
        TrackerEvent score = fixture.Replay.TrackerEvents?.LastOrDefault(e =>
            e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.ScoreResultEvent
        );

        TimeSpan expected = lastVote?.TimeSpan ?? score?.TimeSpan ?? TimeSpan.Zero;

        Assert.True(expected > TimeSpan.Zero);
        Assert.Equal(expected, sessionEnd);
        if (lastVote != null && score != null)
        {
            Assert.Equal(lastVote.TimeSpan, sessionEnd);
        }
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
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
            ParseOptions = new ParseOptionsSettings
            {
                ShouldParseUnits = true,
                ShouldParseStatistics = true,
            },
        };
    }

    private static ReplayAnalyzer CreateAnalyzer(
        AppSettings settings,
        params IFocusCalculator[] calculators
    )
    {
        return new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            null,
            calculators,
            null
        );
    }
}
