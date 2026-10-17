using System.Text.Json;
using HeroesReplay.Core.Status;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Status;

/// <summary><c>status.json</c> keeps its bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class StatusJsonTests
{
    [Fact]
    public void StatusJson_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            },
            SpectatorStatusStore.JsonOptions.SpectatorStatus
        );
}
