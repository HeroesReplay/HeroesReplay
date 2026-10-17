using System.Text.Json;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Dependencies;

/// <summary>The Dependencies files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class DependenciesJsonTests
{
    [Fact]
    public void InstalledJson_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            },
            DependencyInstallRecord.Json.DependencyInstallRecord
        );
}
