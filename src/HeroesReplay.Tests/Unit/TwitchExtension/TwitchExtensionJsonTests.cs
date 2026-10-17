using System.Text.Json;
using HeroesReplay.Core.TwitchExtension;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.TwitchExtension;

/// <summary>The Twitch extension bodies keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchExtensionJsonTests
{
    private static readonly JsonSerializerOptions Before = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Snapshot_KeepsItsBytes() =>
        JsonParity.AssertSameWhole(Before, ExtensionSnapshotSelector.Json.ExtensionSnapshot);

    [Fact]
    public void SnapshotBody_KeepsItsBytes() =>
        JsonParity.AssertSameWhole(Before, ExtensionSnapshotSelector.Json.SnapshotBody);

    [Fact]
    public void WhoAmI_ReadsTheSame() =>
        JsonParity.AssertSame(Before, ExtensionSnapshotSelector.Json.WhoAmIBody);
}
