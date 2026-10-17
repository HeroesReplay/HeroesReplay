using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.Core.YouTube;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// JSON the tests write themselves (#454). Reflection-based System.Text.Json is off in this
/// project, so a fixture in a production format goes through that file's source-generated
/// metadata, and a shape only a test builds names reflection itself.
/// </summary>
internal static class TestJson
{
    /// <summary>Reflection, named, for shapes only a test builds (an anonymous settings file).</summary>
    public static readonly JsonSerializerOptions Reflection = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>A YouTube entry file as the context folder and the uploader write it.</summary>
    public static string Entry(YouTubeEntry entry) =>
        JsonSerializer.Serialize(entry, YouTubeJsonContext.Default.YouTubeEntry);

    /// <summary><paramref name="value"/> by reflection, as its runtime type.</summary>
    public static string Write(object value) =>
        JsonSerializer.Serialize(value, Reflection.GetTypeInfo(value.GetType()));
}
