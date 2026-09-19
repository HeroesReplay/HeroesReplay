using System;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests;

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
    public void AbilityDetectorMatchesAnyBuildEntry()
    {
        Assert.True(
            AbilityDetector.IsBuildInRange(98025, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.True(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68740, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(70000, greaterEqualBuild: 70682, lessThanBuild: 68740)
        );
    }

    [Fact]
    public void AliveFilterTreatsNullTimeSpanDiedAsAlive()
    {
        var living = fixture
            .Replay.Players.SelectMany(p => p.HeroUnits)
            .FirstOrDefault(u => u.TimeSpanDied == null);
        Assert.NotNull(living);
        Assert.True(living.IsAliveAt(living.TimeSpanBorn.Add(TimeSpan.FromSeconds(1))));
        Assert.False(living.IsAliveAt(living.TimeSpanBorn));

        var dead = fixture
            .Replay.Players.SelectMany(p => p.HeroUnits)
            .First(u =>
                u.TimeSpanDied.HasValue
                && u.TimeSpanDied.Value > u.TimeSpanBorn.Add(TimeSpan.FromSeconds(1))
            );
        Assert.True(dead.IsAliveAt(dead.TimeSpanDied.Value - TimeSpan.FromSeconds(1)));
        Assert.False(dead.IsAliveAt(dead.TimeSpanDied.Value));
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
