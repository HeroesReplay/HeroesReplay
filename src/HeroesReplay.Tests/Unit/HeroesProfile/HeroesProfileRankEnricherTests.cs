using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.HeroesProfile.Client.Replay.Item;
using Microsoft.Kiota.Abstractions.Serialization;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileRankEnricherTests
{
    [Theory]
    [InlineData("Platinum", "Gold 3")]
    [InlineData("Diamond", "Bronze 5")]
    [InlineData("Master", "Master")]
    [InlineData("Grandmaster", "Diamond")]
    public void Resolve_StoresHeroesProfileTierInsteadOfFixedLadder(string staleLadder, string tier)
    {
        string rank = HeroesProfileRankEnricher.Resolve("Storm League", staleLadder, tier);

        Assert.Equal(tier, rank);
        Assert.Equal(RankImage.SourceName(tier), RankImage.SourceName(rank));
        Assert.Equal(RankImage.Division(tier), RankImage.Division(rank));
    }

    [Theory]
    [InlineData("Diamond")]
    [InlineData("Platinum")]
    [InlineData("Grandmaster")]
    [InlineData(null)]
    public void Resolve_LeavesRankEmptyWhenLookupFails(string staleLadder)
    {
        Assert.Null(HeroesProfileRankEnricher.Resolve("Storm League", staleLadder, null));
        Assert.Null(HeroesProfileRankEnricher.Resolve("sl", staleLadder, "not-a-league"));
    }

    [Theory]
    [InlineData("Quick Match")]
    [InlineData("ARAM")]
    [InlineData("Unranked Draft")]
    [InlineData(null)]
    public void Resolve_DoesNotApplyStormLeagueTierToOtherModes(string gameType)
    {
        var replay = new HeroesProfileReplay { GameType = gameType, Rank = "Diamond" };

        Assert.False(HeroesProfileRankEnricher.ShouldLookup(replay));
        Assert.Null(HeroesProfileRankEnricher.Resolve(gameType, "Diamond", "Bronze 5"));
        Assert.Null(HeroesProfileRankEnricher.Resolve(gameType, null, "Gold 3"));
    }

    [Theory]
    [InlineData("Gold 3")]
    [InlineData("Bronze 5")]
    [InlineData("platinum-1")]
    public void ShouldLookup_KeepsATierThatAlreadyHasADivision(string rank)
    {
        var replay = new HeroesProfileReplay { GameType = "Storm League", Rank = rank };

        Assert.False(HeroesProfileRankEnricher.ShouldLookup(replay));
        Assert.Equal(rank, HeroesProfileRankEnricher.Resolve("Storm League", rank, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Diamond")]
    [InlineData("master")]
    public void ShouldLookup_ReplacesEmptyOrFixedLadderStormLeagueRanks(string rank)
    {
        var replay = new HeroesProfileReplay { GameType = "Storm League", Rank = rank };

        Assert.True(HeroesProfileRankEnricher.ShouldLookup(replay));
    }

    [Fact]
    public void ToReplay_Detail_StoresAverageMmrAndDoesNotSeedTheFixedLadder()
    {
        var detail = new WithReplayGetResponse
        {
            GameType = "Storm League",
            Players = new UntypedArray(
                new UntypedNode[]
                {
                    new UntypedArray(new UntypedNode[] { Player(2200), Player(2900) }),
                }
            ),
        };

        var replay = HeroesProfileReplayMapper.ToReplay(42, detail);

        Assert.Equal(42, replay.Id);
        Assert.Equal("Storm League", replay.GameType);
        Assert.Equal(2550, replay.AverageMmr);
        Assert.Null(replay.Rank);
        Assert.True(HeroesProfileRankEnricher.ShouldLookup(replay));
    }

    [Fact]
    public void MmrTierPayload_ReadsTierString()
    {
        Assert.Equal(
            "Gold 3",
            MmrTierPayload.Read("{\"game_type\":\"Storm League\",\"mmr\":2561,\"tier\":\"Gold 3\"}")
        );
        Assert.Equal("Bronze 5", MmrTierPayload.Read("\"Bronze 5\""));
        Assert.Equal("Master", MmrTierPayload.Read("Master"));
        Assert.Null(MmrTierPayload.Read("{\"mmr\":2561}"));
        Assert.Null(MmrTierPayload.Read(""));
    }

    [Fact]
    public void CreateMmrTierRequest_CallsStormLeagueTierEndpoint()
    {
        var request = HeroesProfileClient.CreateMmrTierRequest(
            "https://www.heroesprofile.com/api/external/v1/",
            "Storm League",
            2561
        );

        string uri = request.URI.ToString();
        Assert.Contains("/mmr/tier", uri, System.StringComparison.Ordinal);
        Assert.Contains("mmr=2561", uri, System.StringComparison.Ordinal);
        Assert.Contains("game_type=Storm", uri, System.StringComparison.Ordinal);
        Assert.DoesNotContain("1800", uri, System.StringComparison.Ordinal);
    }

    private static UntypedObject Player(int mmr)
    {
        return new UntypedObject(
            new Dictionary<string, UntypedNode> { ["player_mmr"] = new UntypedInteger(mmr) }
        );
    }
}
