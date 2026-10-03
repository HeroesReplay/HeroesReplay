using Heroes.ReplayParser;
using HeroesReplay.Core.Requests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PlayerPriorityRequestTests
{
    [Theory]
    [InlineData("65268119", 65268119, null)]
    [InlineData("65268119,Kazpa#2345", 65268119, "Kazpa#2345")]
    [InlineData("65268119, Kazpa#2345 ", 65268119, "Kazpa#2345")]
    [InlineData("65268119,Sæmund#11", 65268119, "Sæmund#11")]
    public void TryRead_ParsesReplayAndBattleTag(string message, int replayId, string battleTag)
    {
        Assert.True(PlayerPriorityRequest.TryRead(message, out int id, out string tag));
        Assert.Equal(replayId, id);
        Assert.Equal(battleTag, tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("65268119,3")]
    [InlineData("65268119,0")]
    [InlineData("65268119,Kazpa")]
    [InlineData("65268119,Kazpa#")]
    [InlineData("65268119,#2345")]
    [InlineData("65268119,Kazpa#23a5")]
    [InlineData("65268119,Ka zpa#2345")]
    [InlineData("65268119,Kazpa#23#45")]
    [InlineData("65268119,Kazpa#2345,extra")]
    public void TryRead_RejectsAnythingExceptReplayOrReplayAndBattleTag(string message)
    {
        Assert.False(PlayerPriorityRequest.TryRead(message, out _, out _));
    }

    [Fact]
    public void PlayerIndex_FindsTheBattleTagAmongTheReplayPlayers()
    {
        Replay replay = Players(("Alpha", 100), ("Kazpa", 2345), ("Omega", 9));

        int? index = PlayerPriorityRequest.PlayerIndex(
            replay,
            new RewardRequest { BattleTag = "kazpa#2345" }
        );

        Assert.Equal(1, index);
    }

    [Fact]
    public void PlayerIndex_IsNullWhenTheBattleTagDidNotPlay()
    {
        Replay replay = Players(("Alpha", 100), ("Kazpa", 2346));

        Assert.Null(
            PlayerPriorityRequest.PlayerIndex(
                replay,
                new RewardRequest { BattleTag = "Kazpa#2345" }
            )
        );
    }

    [Fact]
    public void PlayerIndex_IsNullWithoutABattleTag()
    {
        Replay replay = Players(("Alpha", 100));

        Assert.Null(PlayerPriorityRequest.PlayerIndex(replay, new RewardRequest()));
        Assert.Null(PlayerPriorityRequest.PlayerIndex(replay, null));
        Assert.Null(
            PlayerPriorityRequest.PlayerIndex(null, new RewardRequest { BattleTag = "Alpha#100" })
        );
    }

    /// <summary>#215: a queue entry saved before the BattleTag change still focuses its slot.</summary>
    [Fact]
    public void PlayerIndex_KeepsTheSlotOfARequestQueuedBeforeTheBattleTagChange()
    {
        Replay replay = Players(("A", 1), ("B", 2), ("C", 3), ("D", 4), ("E", 5));
        RewardRequest legacy = System.Text.Json.JsonSerializer.Deserialize<RewardRequest>(
            """{ "Login": "viewer", "ReplayId": 65268119, "PlayerIndex": 3 }"""
        );

        Assert.Equal(3, PlayerPriorityRequest.PlayerIndex(replay, legacy));
        Assert.Null(
            PlayerPriorityRequest.PlayerIndex(replay, new RewardRequest { LegacyPlayerIndex = 7 })
        );
        Assert.Equal(
            1,
            PlayerPriorityRequest.PlayerIndex(
                replay,
                new RewardRequest { BattleTag = "B#2", LegacyPlayerIndex = 3 }
            )
        );
        Assert.DoesNotContain(
            "PlayerIndex",
            System.Text.Json.JsonSerializer.Serialize(new RewardRequest { BattleTag = "B#2" })
        );
    }

    [Fact]
    public void BlocksBecauseMatchStarted_OnlyForTheReplayAlreadyOnScreen()
    {
        Assert.True(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "TimerDetected", 10));
        Assert.True(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "Loading", 10));
        Assert.False(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "Idle", 10));
        Assert.False(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "TimerDetected", 11));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Watch_FollowsTheHeroWheneverTheyAreAlive(bool alive, bool watch)
    {
        Assert.Equal(watch, PlayerPriorityRequest.Watch(alive));
    }

    private static Replay Players(params (string Name, int Tag)[] players)
    {
        var result = new Player[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            result[i] = new Player { Name = players[i].Name, BattleTag = players[i].Tag };
        }

        return new Replay { Players = result };
    }
}
