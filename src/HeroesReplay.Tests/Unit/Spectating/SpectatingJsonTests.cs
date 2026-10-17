using System.Text.Json;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

/// <summary>The observer panel file keeps its bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectatingJsonTests
{
    [Fact]
    public void ObserverPanels_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            },
            ObserverPanelRequests.Json.StoredPanels
        );
}
