using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights;

/// <summary><c>insights.json</c> keeps its bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class InsightsJsonTests
{
    [Fact]
    public void Insights_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            },
            InsightsJson.Json.MatchInsights
        );
}
