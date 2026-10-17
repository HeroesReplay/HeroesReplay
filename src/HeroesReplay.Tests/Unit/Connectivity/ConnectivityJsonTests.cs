using System.Text.Json;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

/// <summary>The Connectivity files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ConnectivityJsonTests
{
    [Fact]
    public void ReplayResume_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { WriteIndented = true },
            ReplayResumeFile.Json.ReplayResumeRequest
        );
}
