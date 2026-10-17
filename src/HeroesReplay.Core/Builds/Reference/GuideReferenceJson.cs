using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HeroesReplay.Core.Builds.Reference;

/// <summary>
/// The JSON shape of <see cref="GuideReference"/>: camelCase and indented, leaving out defaults
/// (null, false) and empty lists so the checked-in file stays short. Reading fills them back in.
/// </summary>
public static class GuideReferenceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        TypeInfoResolver = BuildsJsonContext.Default.WithAddedModifier(SkipEmptyLists),
    };

    private static readonly JsonTypeInfo<GuideReference> Reference =
        (JsonTypeInfo<GuideReference>)Options.GetTypeInfo(typeof(GuideReference));

    public static GuideReference Parse(string json) =>
        JsonSerializer.Deserialize(json, Reference) ?? new GuideReference();

    public static string Write(GuideReference reference) =>
        JsonSerializer.Serialize(reference ?? new GuideReference(), Reference) + "\n";

    private static void SkipEmptyLists(JsonTypeInfo type)
    {
        foreach (JsonPropertyInfo property in type.Properties)
        {
            if (
                property.PropertyType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
            )
            {
                property.ShouldSerialize = (_, value) =>
                    value is not null and not ICollection { Count: 0 };
            }
        }
    }
}
