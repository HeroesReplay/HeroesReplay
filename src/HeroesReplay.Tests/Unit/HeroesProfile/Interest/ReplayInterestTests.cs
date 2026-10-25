using System.Collections.Generic;
using HeroesReplay.Core.HeroesProfile.Interest;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile.Interest;

/// <summary>A replay's interest from its score screen alone (#510).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayInterestTests
{
    private static readonly ReplayInterestSettings Settings = new();

    [Fact]
    public void ADeathlessCarry_Scores_AndNamesTheHero()
    {
        ReplayInterestScore score = ReplayInterest.Score(
            Game(20, new ReplayScoreLine(0, "Valla", 12, 0, 9, 21, 21, 3, 0)),
            null,
            Settings
        );

        Assert.Equal(Settings.DeathlessCarryPoints, score.Total);
        Assert.Equal(new[] { "deathless carry Valla 12/0/9" }, score.Reasons);
    }

    [Fact]
    public void AKillLeaderWithADeath_IsNotADeathlessCarry()
    {
        ReplayInterestScore score = ReplayInterest.Score(
            Game(20, new ReplayScoreLine(0, "Imperius", 12, 1, 11, 23, 12, 3, 0)),
            null,
            Settings
        );

        Assert.Equal(new[] { "kill leader Imperius 12/1/11" }, score.Reasons);
        Assert.Equal(Settings.KillLeaderPoints, score.Total);
    }

    [Fact]
    public void LengthStreakFireAndAWipe_AllCount()
    {
        ReplayInterestScore score = ReplayInterest.Score(
            Game(27, new ReplayScoreLine(0, "Azmodan", 5, 2, 14, 19, 26, 5, 416)),
            null,
            Settings
        );

        Assert.Equal(
            new[]
            {
                "26 takedown streak Azmodan",
                "416 s on fire Azmodan",
                "team wipe",
                "long game 27 min",
            },
            score.Reasons
        );
        Assert.Equal(
            Settings.LongStreakPoints
                + Settings.OnFirePoints
                + Settings.TeamWipePoints
                + Settings.LongGamePoints,
            score.Total
        );
    }

    [Fact]
    public void AStomp_RanksBelowAnOrdinaryGame()
    {
        ReplayInterestScore stomp = ReplayInterest.Score(Game(11), null, Settings);
        ReplayInterestScore ordinary = ReplayInterest.Score(Game(18), null, Settings);

        Assert.Equal(new[] { "short game 11 min" }, stomp.Reasons);
        Assert.True(stomp.Total < ordinary.Total);
        Assert.Equal(0, ordinary.Total);
    }

    [Theory]
    [InlineData("Diamond 3", true)]
    [InlineData("Master", true)]
    [InlineData("Grandmaster", true)]
    [InlineData("Platinum 1", false)]
    [InlineData(null, false)]
    public void DiamondAndAbove_Count(string rank, bool counts)
    {
        ReplayInterestScore score = ReplayInterest.Score(Game(18), rank, Settings);

        Assert.Equal(counts ? Settings.HighRankPoints : 0, score.Total);
    }

    [Fact]
    public void NoScoreScreen_IsNoInterest()
    {
        Assert.Equal(ReplayInterestScore.None, ReplayInterest.Score(null, "Master", Settings));
    }

    /// <summary>Ten plain lines, the first replaced by <paramref name="star"/>.</summary>
    private static ReplayDetail Game(int minutes, ReplayScoreLine star = null)
    {
        var players = new List<ReplayScoreLine>
        {
            star ?? new ReplayScoreLine(0, "Hero0", 4, 4, 8, 12, 6, 2, 0),
        };
        for (int index = 1; index < 10; index++)
        {
            players.Add(
                new ReplayScoreLine(index < 5 ? 0 : 1, "Hero" + index, 4, 4, 8, 12, 6, 2, 0)
            );
        }

        return new ReplayDetail(1, minutes * 60, players);
    }
}
