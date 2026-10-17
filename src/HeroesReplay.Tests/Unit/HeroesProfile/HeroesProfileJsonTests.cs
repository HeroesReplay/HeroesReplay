using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>The HeroesProfile files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileJsonTests
{
    /// <summary>The options of the player cache before #454.</summary>
    private static readonly JsonSerializerOptions PlayersBefore = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void HeroStatsSnapshot_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { WriteIndented = false },
            HeroStatsJson.FileJson.HeroStatsSnapshot
        );

    [Fact]
    public void PlayerCache_KeepsItsBytes()
    {
        JsonParity.AssertSame(PlayersBefore, PlayerHeroStatsStore.Json.StoredPlayer);
        JsonParity.AssertSame(PlayersBefore, PlayerHeroStatsStore.Json.ListDateTimeOffset);
        JsonParity.AssertSame(PlayersBefore, PlayerHeroStatsStore.Json.PlayerPrivacyCursor);
    }
}
