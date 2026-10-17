using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.Support;

/// <summary>
/// Checks a source-generated JSON path (#454) against the reflection-based options it replaced.
/// <c>before</c> is a copy of the options the file was written with; it runs through an explicit
/// <see cref="DefaultJsonTypeInfoResolver"/>, so it still works with reflection off by default.
/// A sample with every property set must write the same bytes both ways, and reading those bytes
/// back must write them again the same way: a file the old build wrote reads and saves unchanged.
/// Every object in the file must also read the same when its properties are missing (an older
/// file, or a default left out): source generation sets an <c>init</c> property even when the
/// JSON has none, which loses its initializer.
/// </summary>
internal static class JsonParity
{
    public static void AssertSame<T>(JsonSerializerOptions before, JsonTypeInfo<T> after) =>
        AssertSame(before, after, JsonSample.Of<T>());

    public static void AssertSame<T>(JsonSerializerOptions before, JsonTypeInfo<T> after, T sample)
    {
        AssertSameWhole(before, after, sample);
        JsonSerializerOptions reflection = Reflection(before);
        string[] differ = JsonGraph
            .Types(reflection, typeof(T))
            .Where(type => reflection.GetTypeInfo(type).Kind == JsonTypeInfoKind.Object)
            .Where(type =>
                ReadEmpty(reflection, reflection, type)
                != ReadEmpty(after.Options, reflection, type)
            )
            .Select(type => type.FullName)
            .ToArray();
        Assert.True(differ.Length == 0, "These read {} differently: " + string.Join(", ", differ));
    }

    /// <summary>
    /// <see cref="AssertSame{T}(JsonSerializerOptions, JsonTypeInfo{T})"/> without the missing
    /// properties: for JSON written fresh each run and only ever read whole by the same build
    /// (an <c>--output json</c> result, an MCP result, a request body), never an older file.
    /// </summary>
    public static void AssertSameWhole<T>(JsonSerializerOptions before, JsonTypeInfo<T> after) =>
        AssertSameWhole(before, after, JsonSample.Of<T>());

    private static void AssertSameWhole<T>(
        JsonSerializerOptions before,
        JsonTypeInfo<T> after,
        T sample
    )
    {
        JsonSerializerOptions reflection = Reflection(before);
        string expected = JsonSerializer.Serialize(sample, reflection.GetTypeInfo(typeof(T)));
        Assert.Equal(expected, JsonSerializer.Serialize(sample, after));

        object readBefore = JsonSerializer.Deserialize(expected, reflection.GetTypeInfo(typeof(T)));
        Assert.Equal(
            JsonSerializer.Serialize(readBefore, reflection.GetTypeInfo(typeof(T))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize(expected, after), after)
        );
    }

    /// <summary>The bytes <paramref name="before"/> writes for <paramref name="sample"/>: a file the old build wrote.</summary>
    public static string Written<T>(JsonSerializerOptions before, T sample) =>
        JsonSerializer.Serialize(sample, Reflection(before).GetTypeInfo(typeof(T)));

    public static JsonSerializerOptions Reflection(JsonSerializerOptions before) =>
        new(before)
        {
            TypeInfoResolver = before.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
        };

    /// <summary><c>{}</c> read by <paramref name="read"/>, written by <paramref name="write"/>, or the exception's name.</summary>
    private static string ReadEmpty(
        JsonSerializerOptions read,
        JsonSerializerOptions write,
        Type type
    )
    {
        try
        {
            object value = JsonSerializer.Deserialize("{}", read.GetTypeInfo(type));
            return JsonSerializer.Serialize(value, write.GetTypeInfo(type));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return e.GetType().Name;
        }
    }
}
