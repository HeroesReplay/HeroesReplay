using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UnrankedDraftRewardTests
{
    [Theory]
    [InlineData("Random (UD)")]
    [InlineData("Infernal Shrines (UD)")]
    [InlineData("Infernal Shrines (Rank UD)")]
    [InlineData("Rank (UD)")]
    [InlineData("Unranked Draft")]
    [InlineData("  unranked draft  ")]
    public void IsUnrankedDraft_MatchesRemovedMode(string title)
    {
        Assert.True(UnrankedDraftRewardTitles.IsUnrankedDraft(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Random (QM)")]
    [InlineData("Random (SL)")]
    [InlineData("Random (ARAM)")]
    [InlineData("Garden of Terror (QM)")]
    [InlineData("Garden of Terror (Rank QM)")]
    [InlineData("Tomb of the Spider Queen (SL)")]
    [InlineData("Lost Cavern (Rank ARAM)")]
    [InlineData("ReplayId")]
    [InlineData("ReplayId + YouTube")]
    public void IsUnrankedDraft_LeavesQuickMatchStormLeagueAndAram(string title)
    {
        Assert.False(UnrankedDraftRewardTitles.IsUnrankedDraft(title));
    }

    [Fact]
    public void Catalog_HasQuickMatchMapRewardsAndNoUnrankedDraft()
    {
        var gameData = new MapOnlyGameData(
            new Map("Garden of Terror", "Garden of Terror", false, "standard", true),
            new Map("Cursed Hollow", "Cursed Hollow", true, "standard", true),
            new Map("Lost Cavern", "Lost Cavern", false, "ARAM", true),
            new Map("Escape From Braxis", "Escape From Braxis", false, "brawl", false)
        );
        var holder = new SupportedRewardsHolder(gameData);

        Assert.Contains(holder.Rewards, reward => reward.Title == "Garden of Terror (QM)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Garden of Terror (Rank QM)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Cursed Hollow (SL)");
        Assert.Contains(holder.Rewards, reward => reward.Title == "Lost Cavern (ARAM)");
        Assert.DoesNotContain(holder.Rewards, reward => reward.Mode == GameType.UnrankedDraft);
        Assert.DoesNotContain(
            holder.Rewards,
            reward => UnrankedDraftRewardTitles.IsUnrankedDraft(reward.Title)
        );
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
