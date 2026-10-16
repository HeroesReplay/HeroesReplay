using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus.Cards;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ScoreStatsTests
{
    [Fact]
    public void EveryNumericScoreResultField_IsReadUnderItsOwnName()
    {
        string[] numeric = typeof(ScoreResult)
            .GetProperties()
            .Where(property =>
                property.PropertyType == typeof(int)
                || property.PropertyType == typeof(int?)
                || property.PropertyType == typeof(TimeSpan)
                || property.PropertyType == typeof(TimeSpan?)
            )
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(numeric.OrderBy(name => name), ScoreStats.Names.OrderBy(name => name));
    }

    [Fact]
    public void Read_GivesTimesInSeconds_AndLeavesEmptyStatsOut()
    {
        var score = new ScoreResult
        {
            SoloKills = 7,
            MercCampCaptures = 4,
            TimeSpentDead = TimeSpan.FromSeconds(95),
            TimeCCdEnemyHeroes = TimeSpan.FromSeconds(42.5),
            Healing = null,
            MatchAwards = new List<MatchAwardType>
            {
                MatchAwardType.MVP,
                MatchAwardType.MostMercCampsCaptured,
                MatchAwardType.MVP,
            },
        };

        Dictionary<string, double> stats = ScoreStats.Read(score);

        Assert.Equal(7, stats["SoloKills"]);
        Assert.Equal(4, stats["MercCampCaptures"]);
        Assert.Equal(95, stats["TimeSpentDead"]);
        Assert.Equal(42.5, stats["TimeCCdEnemyHeroes"]);
        Assert.False(stats.ContainsKey("Healing"));
        Assert.Equal(new[] { "MVP", "MostMercCampsCaptured" }, ScoreStats.Awards(score));
        Assert.Empty(ScoreStats.Read(null));
        Assert.Empty(ScoreStats.Awards(null));
    }

    [Fact]
    public void IsStatAndIsAward_AcceptOnlyRealNames()
    {
        Assert.True(ScoreStats.IsStat("ClutchHealsPerformed"));
        Assert.False(ScoreStats.IsStat("clutchHealsPerformed"));
        Assert.False(ScoreStats.IsStat("MatchAwards"));
        Assert.True(ScoreStats.IsAward("ClutchHealer"));
        Assert.False(ScoreStats.IsAward("ClutchHeals"));
        Assert.False(ScoreStats.IsAward("3"));
    }
}
