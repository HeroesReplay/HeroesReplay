using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Reference;

/// <summary>The talent-build reference and the guide extract keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class BuildsJsonTests
{
    /// <summary><see cref="GuideReferenceJson"/>'s options before #454, by reflection.</summary>
    private static readonly JsonSerializerOptions ReferenceBefore = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyLists } },
    };

    [Fact]
    public void Reference_KeepsItsBytes() =>
        JsonParity.AssertSame(
            ReferenceBefore,
            (JsonTypeInfo<GuideReference>)
                GuideReferenceJson.Options.GetTypeInfo(typeof(GuideReference))
        );

    [Fact]
    public void EmbeddedReference_ReadsAndWritesBackByteForByte()
    {
        using Stream stream = typeof(GuideCatalog).Assembly.GetManifestResourceStream(
            GuideCatalog.ResourceName
        );
        using var reader = new StreamReader(stream);
        string file = reader.ReadToEnd();
        string written = GuideReferenceJson.Write(GuideReferenceJson.Parse(file));

        Assert.Equal(file.ReplaceLineEndings("\n"), written.ReplaceLineEndings("\n"));
        Assert.Equal(
            JsonSerializer.Serialize(
                JsonSerializer.Deserialize<GuideReference>(file, ReferenceBefore),
                ReferenceBefore
            ) + "\n",
            written
        );
    }

    [Fact]
    public void GuideExtract_ReadsTheSame() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            GuideExtractJson.Json.GuideExtractHeroArray
        );

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
