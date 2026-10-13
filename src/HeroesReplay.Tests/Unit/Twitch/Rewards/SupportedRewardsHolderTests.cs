using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesData.Rotation;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.Rewards;
using TwitchLib.PubSub.Events;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Twitch.Rewards;

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

    /// <summary>
    /// #425: Warhead Junction is out of the Storm League pool in the shipped catalog's fallback,
    /// so it has Quick Match rewards and its Storm League rewards are retired.
    /// </summary>
    [Fact]
    public void LiveCatalog_WarheadJunctionIsAQuickMatchMap()
    {
        var holder = new SupportedRewardsHolder(new MapOnlyGameData(LoadCatalog().ToArray()));

        Assert.Contains(holder.Rewards, reward => reward.Title == "Warhead Junction (QM)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Warhead Junction (Rank QM)");
        Assert.DoesNotContain(holder.Rewards, reward => reward.Title == "Warhead Junction (SL)");
        Assert.Contains("Warhead Junction (SL)", holder.RetiredTitles);
        Assert.Contains("Warhead Junction (Rank SL)", holder.RetiredTitles);
        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Warhead Junction (Rank QM)" },
                out SupportedReward reward
            )
        );
        Assert.Equal("Warhead Junction", reward.Map);
        Assert.Equal(GameType.QuickMatch, reward.Mode);
    }

    /// <summary>#411: one Blind Rating reward, Storm League, no viewer input.</summary>
    [Fact]
    public void BlindRating_IsOneStormLeagueRewardWithoutInput()
    {
        var holder = new SupportedRewardsHolder(new MapOnlyGameData(LoadCatalog().ToArray()));

        SupportedReward blind = Assert.Single(
            holder.Rewards,
            reward => reward.RewardType.HasFlag(RewardType.Blind)
        );
        Assert.Equal(SupportedRewardsHolder.BlindRatingTitle, blind.Title);
        Assert.Equal(RewardType.SL | RewardType.Blind, blind.RewardType);
        Assert.Equal(GameType.StormLeague, blind.Mode);
        Assert.Null(blind.Map);
        Assert.False(blind.IsUserInputRequired);
        Assert.Equal(
            "Queues a random recent Storm League replay with its rank hidden. Guess the rating, Bronze to Master, before it is revealed at the end of the match.",
            blind.Prompt
        );
        Assert.True(
            holder.TryGetReward(
                new OnRewardRedeemedArgs { RewardTitle = "Blind Rating" },
                out SupportedReward found
            )
        );
        Assert.Same(blind, found);
    }

    [Fact]
    public void BlindRating_IsHandledByTheMapRewardHandler()
    {
        var handler = new HeroesReplay.Core.Twitch.RedeemedRewards.MapRewardHandler(
            null,
            null,
            null,
            null,
            null
        );

        Assert.Contains(RewardType.SL | RewardType.Blind, handler.Supports);
    }

    /// <summary>Twitch allows 50 custom rewards per channel.</summary>
    [Fact]
    public void LiveCatalog_StaysUnderTheTwitchRewardLimit()
    {
        var holder = new SupportedRewardsHolder(new MapOnlyGameData(LoadCatalog().ToArray()));

        Assert.InRange(holder.Rewards.Count, 1, 50);
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

    /// <summary>
    /// #423: every reward the sync writes to the channel says what it does, within Twitch's
    /// prompt limit, for every map in the shipped catalog.
    /// </summary>
    [Fact]
    public void Rewards_EveryCatalogRewardHasADescriptionTwitchAccepts()
    {
        var holder = new SupportedRewardsHolder(new MapOnlyGameData(LoadCatalog().ToArray()));

        Assert.NotEmpty(holder.Rewards);
        foreach (SupportedReward reward in holder.Rewards)
        {
            Assert.False(string.IsNullOrWhiteSpace(reward.Prompt), reward.Title);
            Assert.True(
                reward.Prompt.Length <= RewardDescription.MaxLength,
                $"{reward.Title}: {reward.Prompt.Length} characters"
            );
            Assert.StartsWith("Queues ", reward.Prompt);
        }
    }

    [Fact]
    public void Rewards_DescriptionsSayWhatEachKindQueues()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Cursed Hollow", "CursedHollow", true, "standard", true),
                new Map("Silver City", "SilverCity", false, "ARAM", true)
            )
        );

        string Prompt(string title) =>
            holder.Rewards.Single(reward => reward.Title == title).Prompt;

        Assert.Equal(
            "Queues a random recent Quick Match replay to watch on stream.",
            Prompt("Random (QM)")
        );
        Assert.Equal(
            "Queues a random recent ARAM replay on Silver City to watch on stream.",
            Prompt("Silver City (ARAM)")
        );
        Assert.Equal(
            "Queues a recent Storm League replay on Cursed Hollow at the rank you enter: Bronze, Silver, Gold, Platinum, Diamond, Master (no division).",
            Prompt("Cursed Hollow (Rank SL)")
        );
        Assert.Equal(
            "Queues the replay you choose. " + RewardDescription.ReplayIdInput,
            Prompt("ReplayId")
        );
        Assert.EndsWith("Recorded and uploaded to YouTube.", Prompt("ReplayId + YouTube"));
    }

    [Fact]
    public void Rewards_OnlyRankTitlesAskForARank()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Braxis Holdout", "BraxisHoldout", true, "standard", true),
                new Map("Silver City", "SilverCity", true, "aram", false)
            )
        );

        Assert.NotEmpty(holder.Rewards);
        foreach (SupportedReward reward in holder.Rewards)
        {
            bool rankTitle = reward.Title.Contains("(Rank ", StringComparison.Ordinal);
            Assert.True(
                rankTitle == reward.RewardType.HasFlag(RewardType.Rank),
                $"{reward.Title} is {reward.RewardType}"
            );
            Assert.Equal(
                rankTitle || reward.RewardType == RewardType.ReplayId,
                reward.IsUserInputRequired
            );
        }
    }

    [Fact]
    public void RewardType_ModeMapAndRankCombinationsAreDistinct()
    {
        RewardType[] modes = { RewardType.ARAM, RewardType.QM, RewardType.UD, RewardType.SL };
        var values = new HashSet<RewardType> { RewardType.ReplayId };
        foreach (RewardType mode in modes)
        {
            Assert.True(values.Add(mode), mode.ToString());
            Assert.True(values.Add(mode | RewardType.Map), mode + " | Map");
            Assert.True(
                values.Add(mode | RewardType.Map | RewardType.Rank),
                mode + " | Map | Rank"
            );
            Assert.False((mode | RewardType.Map).HasFlag(RewardType.Rank), mode + " | Map");
            Assert.True(values.Add(mode | RewardType.Blind), mode + " | Blind");
            Assert.False((mode | RewardType.Map).HasFlag(RewardType.Blind), mode + " | Map");
        }
    }

    [Theory]
    [InlineData("\"QM, Map\"", RewardType.QM | RewardType.Map)]
    [InlineData("\"SL, Map, Rank\"", RewardType.SL | RewardType.Map | RewardType.Rank)]
    [InlineData("\"ARAM, Map\"", RewardType.ARAM | RewardType.Map)]
    [InlineData("\"ReplayId\"", RewardType.ReplayId)]
    [InlineData("\"SL, Blind\"", RewardType.SL | RewardType.Blind)]
    public void RewardType_StoredNamesParseToTheIntendedValue(string json, RewardType expected)
    {
        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter(
                    allowIntegerValues: true
                ),
            },
        };

        Assert.Equal(expected, JsonSerializer.Deserialize<RewardType>(json, options));
    }

    /// <summary>
    /// #425: the Storm League pool, not the catalog flag, decides which maps get Storm League
    /// rewards. A map out of the pool gets Quick Match rewards instead, and its Storm League
    /// rewards are retired so the sync deletes them from the channel.
    /// </summary>
    [Fact]
    public void Rotation_MapsOutOfThePoolGetQuickMatchRewards_AndRetireTheirStormLeagueOnes()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Cursed Hollow", "CursedHollow", false, "standard", true),
                new Map("Hanamura Temple", "Hanamura", true, "standard", true),
                new Map("Silver City", "SilverCity", false, "ARAM", true)
            ),
            new FixedRotation("Cursed Hollow")
        );
        List<string> titles = holder.Rewards.Select(reward => reward.Title).ToList();

        Assert.Contains("Cursed Hollow (SL)", titles);
        Assert.Contains("Cursed Hollow (Rank SL)", titles);
        Assert.DoesNotContain("Cursed Hollow (QM)", titles);
        Assert.Contains("Hanamura Temple (QM)", titles);
        Assert.Contains("Hanamura Temple (Rank QM)", titles);
        Assert.DoesNotContain("Hanamura Temple (SL)", titles);
        Assert.DoesNotContain("Hanamura Temple (Rank SL)", titles);
        Assert.Contains("Silver City (ARAM)", titles);
        Assert.Equal(
            new[] { "Hanamura Temple (SL)", "Hanamura Temple (Rank SL)" },
            holder.RetiredTitles
        );
        Assert.Equal("pool: Cursed Hollow", holder.PoolSummary);
    }

    [Fact]
    public void Rotation_WithoutOne_TheCatalogFlagDecides()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Cursed Hollow", "CursedHollow", true, "standard", true),
                new Map("Hanamura Temple", "Hanamura", false, "standard", true)
            )
        );

        Assert.Contains(holder.Rewards, reward => reward.Title == "Cursed Hollow (SL)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Hanamura Temple (QM)");
        Assert.Equal(
            new[] { "Hanamura Temple (SL)", "Hanamura Temple (Rank SL)" },
            holder.RetiredTitles
        );
    }

    private sealed class FixedRotation : IStormLeagueRotation
    {
        private readonly HashSet<string> pool;

        public FixedRotation(params string[] maps)
        {
            pool = new HashSet<string>(maps, StringComparer.Ordinal);
        }

        public bool Contains(Map map) => map != null && pool.Contains(map.Name);

        public string Describe(IEnumerable<Map> catalog) =>
            "pool: " + string.Join(", ", pool.OrderBy(name => name, StringComparer.Ordinal));
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
