using System;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StandardCatchUpTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    [Fact]
    public void JumpTo_MovesAThreeDayOldCursorNearTheNewestId()
    {
        var stale = Replay(65580001, "2026-09-30 18:00:00");

        Assert.Equal(
            65657000,
            StandardCatchUp.JumpTo(65580000, stale, 65660000, Now, MaxAge, 3000)
        );
    }

    [Fact]
    public void JumpTo_KeepsARecentCandidate()
    {
        var recent = Replay(65658000, "2026-10-03 16:00:00");

        Assert.Null(StandardCatchUp.JumpTo(65657000, recent, 65660000, Now, MaxAge, 3000));
    }

    [Fact]
    public void JumpTo_NeverMovesTheCursorBackwards()
    {
        var stale = Replay(65659500, "2026-09-30 18:00:00");

        Assert.Null(StandardCatchUp.JumpTo(65659000, stale, 65660000, Now, MaxAge, 3000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void JumpTo_KeepsACandidateWithNoGameDate(string gameDate)
    {
        Assert.Null(
            StandardCatchUp.JumpTo(
                65580000,
                Replay(65580001, gameDate),
                65660000,
                Now,
                MaxAge,
                3000
            )
        );
    }

    [Fact]
    public void JumpTo_IsOffWhenTheMaxAgeIsZeroOrTheNewestIdIsUnknown()
    {
        var stale = Replay(65580001, "2026-09-30 18:00:00");

        Assert.Null(StandardCatchUp.JumpTo(65580000, stale, 65660000, Now, TimeSpan.Zero, 3000));
        Assert.Null(StandardCatchUp.JumpTo(65580000, stale, 0, Now, MaxAge, 3000));
    }

    [Fact]
    public void Age_ReadsTheGameDateAsUtc()
    {
        Assert.Equal(
            TimeSpan.FromHours(2),
            StandardCatchUp.Age(Replay(1, "2026-10-03 16:00:00"), Now)
        );
    }

    private static HeroesProfileReplay Replay(int id, string gameDate) =>
        new() { Id = id, GameDate = gameDate };
}
