using System;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchPredictionTests : IClassFixture<ReplayFixture>
{
    private readonly ReplayFixture fixture;

    public MatchPredictionTests(ReplayFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void TitleForMap_FitsHelixLimit()
    {
        Assert.Equal("Who wins?", MatchPrediction.TitleForMap(null));
        Assert.Equal("Alterac Pass: who wins?", MatchPrediction.TitleForMap("Alterac Pass"));
        Assert.True(
            MatchPrediction.TitleForMap("Alterac Pass").Length <= MatchPrediction.MaxTitleLength
        );
    }

    [Fact]
    public void WindowSeconds_ClampsHelixRange()
    {
        Assert.Equal(30, MatchPrediction.WindowSeconds(TimeSpan.FromSeconds(5)));
        Assert.Equal(120, MatchPrediction.WindowSeconds(TimeSpan.FromMinutes(2)));
        Assert.Equal(1800, MatchPrediction.WindowSeconds(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void OutcomeTitle_BlueIsTeam0()
    {
        Assert.Equal("Blue", MatchPrediction.OutcomeTitle(0));
        Assert.Equal("Red", MatchPrediction.OutcomeTitle(1));
    }

    [Fact]
    public void WinningTeam_FromParsedReplay()
    {
        int? team = TwitchMatchPredictionService.WinningTeam(fixture.Replay);
        Assert.True(team == 0 || team == 1, $"expected team 0 or 1, got {team}");
        Assert.All(
            fixture.Replay.Players,
            player =>
            {
                if (player.IsWinner)
                {
                    Assert.Equal(team, player.Team);
                }
            }
        );
    }
}
