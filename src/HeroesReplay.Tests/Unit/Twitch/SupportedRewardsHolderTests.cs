using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch.Rewards;
using TwitchLib.PubSub.Events;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SupportedRewardsHolderTests
{
    [Fact]
    public void TryGetReward_RankQuickMatchOnRankedMap_IsHandledButNotCreated()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Warhead Junction", "Warhead Junction", true, "standard", true)
            )
        );

        Assert.DoesNotContain(
            holder.Rewards,
            reward => reward.Title == "Warhead Junction (Rank QM)"
        );
        Assert.DoesNotContain(holder.Rewards, reward => reward.Title == "Warhead Junction (QM)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Warhead Junction (Rank SL)");

        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Warhead Junction (Rank QM)" },
                out SupportedReward rank
            )
        );
        Assert.Equal(RewardType.QM | RewardType.Map | RewardType.Rank, rank.RewardType);
        Assert.Equal(GameType.QuickMatch, rank.Mode);
        Assert.Equal("Warhead Junction", rank.Map);
        Assert.True(rank.IsUserInputRequired);

        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "  warhead junction (qm)  " },
                out SupportedReward quick
            )
        );
        Assert.Equal(GameType.QuickMatch, quick.Mode);
        Assert.Equal(RewardType.QM | RewardType.Map, quick.RewardType);
    }

    [Fact]
    public void TryGetReward_CatalogStormLeagueRank_IsTheSyncedReward()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(new Map("Braxis Holdout", "BraxisHoldout", true, "standard", true))
        );
        SupportedReward catalog = holder.Rewards.Single(reward =>
            reward.Title == "Braxis Holdout (Rank SL)"
        );

        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Braxis Holdout (Rank SL)" },
                out SupportedReward found
            )
        );
        Assert.Same(catalog, found);
    }

    [Fact]
    public void TryGetReward_IgnoresUnplayableMapsAndTheWrongMode()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Escape From Braxis", "EscapeFromBraxis", false, "brawl", false),
                new Map("Lost Cavern", "LostCavern", false, "ARAM", true)
            )
        );

        Assert.False(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Escape From Braxis (QM)" },
                out _
            )
        );
        Assert.False(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Lost Cavern (Rank QM)" },
                out _
            )
        );
        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Lost Cavern (Rank ARAM)" },
                out SupportedReward aram
            )
        );
        Assert.Equal(GameType.ARAM, aram.Mode);
    }

    [Fact]
    public void LiveCatalog_AcceptsRankQuickMatchForWarheadJunction()
    {
        var holder = new SupportedRewardsHolder(new MapOnlyGameData(LoadCatalog().ToArray()));

        Assert.DoesNotContain(
            holder.Rewards,
            reward => reward.Title == "Warhead Junction (Rank QM)"
        );
        Assert.Contains(holder.Rewards, reward => reward.Title == "Warhead Junction (SL)");
        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Warhead Junction (Rank QM)" },
                out SupportedReward reward
            )
        );
        Assert.Equal("Warhead Junction", reward.Map);
        Assert.Equal(GameType.QuickMatch, reward.Mode);
    }

    private static List<Map> LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        var maps = new List<Map>();
        foreach (
            JsonElement item in document
                .RootElement.GetProperty("Maps")
                .GetProperty("Catalog")
                .EnumerateArray()
        )
        {
            maps.Add(
                new Map(
                    item.GetProperty("Name").GetString(),
                    item.GetProperty("ShortName").GetString(),
                    item.GetProperty("RankedRotation").GetBoolean(),
                    item.GetProperty("Type").GetString(),
                    item.GetProperty("Playable").GetBoolean()
                )
            );
        }

        return maps;
    }

    private sealed class MapOnlyGameData : IGameData
    {
        public MapOnlyGameData(params Map[] maps)
        {
            Maps = maps;
        }

        public IReadOnlyDictionary<string, UnitGroup> UnitGroups => null;

        public IReadOnlyList<Hero> Heroes => null;

        public IReadOnlyCollection<string> CoreUnits => null;

        public IReadOnlyCollection<string> BossUnits => null;

        public IReadOnlyCollection<string> VehicleUnits => null;

        public IReadOnlyList<Map> Maps { get; }

        public UnitGroup GetUnitGroup(string unitName) => default;

        public Task LoadDataAsync() => Task.CompletedTask;
    }
}
