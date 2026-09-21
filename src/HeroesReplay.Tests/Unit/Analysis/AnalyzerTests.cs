using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Models;
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

    [Fact]
    public void GetWatchUntil_HoldsAfterCore_TrackerCanExtendNotShorten()
    {
        TimeSpan core = TimeSpan.FromMinutes(17);
        TimeSpan hold = TimeSpan.FromMinutes(1);
        TimeSpan length = TimeSpan.FromMinutes(20);

        Assert.Equal(
            TimeSpan.FromMinutes(18),
            ReplayAnalyzer.GetWatchUntil(core, TimeSpan.FromMinutes(17.3), hold, length)
        );
        Assert.Equal(
            TimeSpan.FromMinutes(18.5),
            ReplayAnalyzer.GetWatchUntil(core, TimeSpan.FromMinutes(18.5), hold, length)
        );
        Assert.Equal(
            TimeSpan.FromMinutes(17.5),
            ReplayAnalyzer.GetWatchUntil(
                core,
                TimeSpan.FromMinutes(17.2),
                hold,
                TimeSpan.FromMinutes(17.5)
            )
        );
        Assert.Equal(
            TimeSpan.Zero,
            ReplayAnalyzer.GetWatchUntil(TimeSpan.Zero, core, hold, length)
        );
    }

    [Fact]
    public void FocusTimeline_IsContiguousFromFirstSelectionToEnd()
    {
        var settings = CreateSettings();
        var analyzer = CreateAnalyzer(settings, new KillCalculator(settings));

        var results = analyzer.GetPlayers(fixture.Replay);
        Assert.NotEmpty(results);

        var times = results.Keys.OrderBy(t => t).ToList();
        TimeSpan expected = times[0];
        foreach (TimeSpan time in times)
        {
            Assert.Equal(expected, time);
            expected = expected.Add(TimeSpan.FromSeconds(1));
        }

        int lastIndex = int.MinValue;
        int swaps = 0;
        foreach (TimeSpan time in times)
        {
            Focus focus = results[time];
            if (focus.Index != lastIndex)
            {
                swaps++;
                lastIndex = focus.Index;
            }
        }

        Assert.True(swaps >= 1);
        Assert.True(swaps < times.Count);
    }

    [Fact]
    public void KillStreak_HoldsKillerAcrossSpacedKills()
    {
        var settings = CreateSettings();
        var analyzer = CreateAnalyzer(settings, new KillCalculator(settings));
        var results = analyzer.GetPlayers(fixture.Replay);

        var streak = fixture
            .Replay.Players.SelectMany(player =>
                player.HeroUnits ?? new List<Heroes.ReplayParser.Unit>()
            )
            .Where(unit => unit.TimeSpanDied.HasValue && unit.PlayerKilledBy != null)
            .GroupBy(unit => unit.PlayerKilledBy)
            .Select(group =>
            {
                var seconds = group
                    .Select(unit => unit.TimeSpanDied.Value.FloorSeconds())
                    .OrderBy(second => second)
                    .ToList();
                KillStreak best = KillStreaks
                    .Group(seconds, (int)settings.Spectate.KillStreakWindow.TotalSeconds)
                    .OrderByDescending(item => item.Kills)
                    .ThenByDescending(item => item.EndSecond - item.StartSecond)
                    .First();
                return (Killer: group.Key, Streak: best);
            })
            .OrderByDescending(item => item.Streak.Kills)
            .ThenByDescending(item => item.Streak.EndSecond - item.Streak.StartSecond)
            .First();

        Assert.True(streak.Streak.Kills >= 2, "expected a multi-kill streak in the sample replay.");

        int hold = (int)settings.Spectate.KillStreakHoldTime.TotalSeconds;
        TimeSpan start = TimeSpan.FromSeconds(streak.Streak.StartSecond);
        TimeSpan end = TimeSpan.FromSeconds(streak.Streak.EndSecond + hold);
        for (TimeSpan time = start; time <= end; time = time.Add(TimeSpan.FromSeconds(1)))
        {
            Assert.True(results.TryGetValue(time, out Focus focus), $"missing focus at {time}");
            Assert.Equal(streak.Killer, focus.Target);
            Assert.Equal(typeof(KillCalculator), focus.Calculator);
            Assert.True(focus.Points > settings.Weights.PlayerKill);
        }
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            Weights = new WeightSettings
            {
                PlayerKill = 10,
                KillStreakBonus = 1.5f,
                PentaKill = 16,
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
                KillStreakWindow = TimeSpan.FromSeconds(12),
                KillStreakHoldTime = TimeSpan.FromSeconds(5),
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
