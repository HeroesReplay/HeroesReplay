using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>Whom the camera follows for a whole replay: a request's player, else the focus pick (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayFocusTests
{
    private static readonly FocusPick Pick = new(
        1,
        "1",
        3,
        "Star#1234",
        "Li-Ming",
        "Wiza",
        "Ranged Assassin",
        null
    );

    [Fact]
    public void TheFocusPick_IsFollowed_WhenItsIndexIsInTheReplay()
    {
        Assert.Equal(3, ReplayFocus.PlayerIndex(Loaded(Pick)));
        Assert.Null(ReplayFocus.PlayerIndex(Loaded(Pick with { PlayerIndex = 10 })));
        Assert.Null(ReplayFocus.PlayerIndex(Loaded(null)));
        Assert.Null(ReplayFocus.PlayerIndex(null));
    }

    [Fact]
    public void ARequest_DecidesItsOwnCamera_EvenWithAFocusPick()
    {
        LoadedReplay named = Loaded(Pick);
        named.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest { BattleTag = "P5#1005" },
        };
        LoadedReplay unnamed = Loaded(Pick);
        unnamed.RewardQueueItem = new RewardQueueItem { Request = new RewardRequest() };

        Assert.Equal(5, ReplayFocus.PlayerIndex(named));
        Assert.Null(ReplayFocus.PlayerIndex(unnamed));
    }

    private static LoadedReplay Loaded(FocusPick pick)
    {
        var players = new Player[10];
        for (int index = 0; index < players.Length; index++)
        {
            players[index] = new Player { Name = "P" + index, BattleTag = 1000 + index };
        }

        return new LoadedReplay
        {
            ReplayId = 1,
            Replay = new Replay { Players = players },
            FocusPick = pick,
        };
    }
}
