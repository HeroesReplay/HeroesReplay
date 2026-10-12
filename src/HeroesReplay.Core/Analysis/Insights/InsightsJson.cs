using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>The <c>insights.json</c> shape: camelCase, indented, enums by name.</summary>
public static class InsightsJson
{
    public const string FileName = "insights.json";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(MatchInsights insights) =>
        JsonSerializer.Serialize(insights ?? new MatchInsights(), Options) + "\n";

    public static MatchInsights Parse(string json) =>
        JsonSerializer.Deserialize<MatchInsights>(json, Options) ?? new MatchInsights();
}
