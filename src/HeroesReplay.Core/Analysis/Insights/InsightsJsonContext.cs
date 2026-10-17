using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>
/// Source-generated JSON for <c>insights.json</c> (#454): no reflection, so Native AOT reads and
/// writes it. A property with an initializer is <c>set</c>, not <c>init</c>: source generation
/// passes every <c>init</c> property to the object initializer, so one an older file lacks would
/// read back as null or zero instead of its initializer.
/// </summary>
[JsonSerializable(typeof(MatchInsights))]
internal sealed partial class InsightsJsonContext : JsonSerializerContext;
