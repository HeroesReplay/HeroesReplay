using System.Text.Json;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>The GameClient files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameClientJsonTests
{
    [Fact]
    public void ClientDownloadHolds_KeepTheirBytes()
    {
        JsonParity.AssertSame(
            new JsonSerializerOptions { WriteIndented = true },
            ClientDownloadHold.Json.SortedDictionaryStringDateTimeOffset
        );
        JsonParity.AssertSame(
            new JsonSerializerOptions(),
            GameClientJsonContext.Default.DictionaryStringDateTimeOffset
        );
    }
}
