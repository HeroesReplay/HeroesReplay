using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Analysis.Insights.Awards;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Players;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>The <c>insights.json</c> shape: camelCase, indented, enums by name.</summary>
public static class InsightsJson
{
    public const string FileName = "insights.json";

    internal static readonly InsightsJsonContext Json = new(
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter<LaneConfidence>(),
                new JsonStringEnumConverter<StandoutKind>(),
                new JsonStringEnumConverter<AwardKind>(),
                new JsonStringEnumConverter<BossFightKind>(),
            },
        }
    );

    public static string Write(MatchInsights insights) =>
        JsonSerializer.Serialize(insights ?? new MatchInsights(), Json.MatchInsights) + "\n";

    public static MatchInsights Parse(string json) =>
        JsonSerializer.Deserialize(json, Json.MatchInsights) ?? new MatchInsights();
}
