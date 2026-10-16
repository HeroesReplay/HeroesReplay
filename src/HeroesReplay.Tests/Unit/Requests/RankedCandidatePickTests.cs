using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

/// <summary>
/// #429: a rank reward looks candidates up one at a time and queues the first at the entered
/// rank. Before, the rank was only part of a cache key and any game on the map was queued.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RankedCandidatePickTests
{
    [Fact]
    public async Task TheCandidateAtTheEnteredRank_IsPicked_WithItsTier()
    {
        var tiers = new Dictionary<int, string>
        {
            [1] = "Bronze 2",
            [2] = "Platinum 4",
            [3] = "Silver 1",
        };

        RankedPick pick = await RankedCandidatePick.FindAsync(
            Candidates(1, 2, 3),
            GameRank.Platinum,
            (replay, _) => Task.FromResult(tiers[replay.Id]),
            limit: 10,
            new Random(7),
            CancellationToken.None
        );

        Assert.Equal(2, pick.Replay.Id);
        Assert.Equal("Platinum 4", pick.Tier);
        Assert.False(pick.TimedOut);
    }

    [Fact]
    public async Task NoMatch_StopsAtTheLimit()
    {
        int asked = 0;

        RankedPick pick = await RankedCandidatePick.FindAsync(
            Candidates(Enumerable.Range(1, 10).ToArray()),
            GameRank.Master,
            (_, _) =>
            {
                asked++;
                return Task.FromResult("Gold 1");
            },
            limit: 3,
            new Random(7),
            CancellationToken.None
        );

        Assert.Null(pick.Replay);
        Assert.Equal(3, pick.Looked);
        Assert.Equal(3, asked);
    }

    [Theory]
    [InlineData("Grandmaster", GameRank.Master)]
    [InlineData("Master", GameRank.Master)]
    [InlineData(" diamond 5 ", GameRank.Diamond)]
    public async Task HeroesProfileTiers_CountForTheirLeague(string tier, GameRank rank)
    {
        RankedPick pick = await RankedCandidatePick.FindAsync(
            Candidates(1),
            rank,
            (_, _) => Task.FromResult(tier),
            limit: 5,
            new Random(7),
            CancellationToken.None
        );

        Assert.Equal(1, pick.Replay?.Id);
        Assert.Equal(tier.Trim(), pick.Tier);
    }

    [Fact]
    public async Task AnUnknownTier_IsSkipped()
    {
        RankedPick pick = await RankedCandidatePick.FindAsync(
            Candidates(1, 2),
            GameRank.Gold,
            (replay, _) => Task.FromResult(replay.Id == 1 ? null : "Gold 4"),
            limit: 5,
            new Random(7),
            CancellationToken.None
        );

        Assert.Equal(2, pick.Replay.Id);
    }

    [Fact]
    public async Task ASearchThatRunsOutOfTime_EndsWithoutAPick()
    {
        using var timeout = new CancellationTokenSource();

        RankedPick pick = await RankedCandidatePick.FindAsync(
            Candidates(1, 2, 3),
            GameRank.Gold,
            (_, token) =>
            {
                timeout.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult("Gold 1");
            },
            limit: 10,
            new Random(7),
            timeout.Token
        );

        Assert.Null(pick.Replay);
        Assert.True(pick.TimedOut);
        Assert.Equal(1, pick.Looked);
    }

    private static List<HeroesProfileReplay> Candidates(params int[] ids) =>
        ids.Select(id => new HeroesProfileReplay { Id = id, Map = "Cursed Hollow" }).ToList();
}
