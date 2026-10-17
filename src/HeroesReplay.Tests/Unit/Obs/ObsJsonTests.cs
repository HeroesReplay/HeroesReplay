using System.Text.Encodings.Web;
using System.Text.Json;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>The Obs files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsJsonTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public void BundleManifest_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            },
            ObsCollectionBundle.Json.ObsBundleManifest
        );

    [Fact]
    public void DiffStrings_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
            ObsCollectionDiff.Strings.String
        );

    [Fact]
    public void CollectionPathStrings_KeepTheirBytes() =>
        JsonParity.AssertSame(new JsonSerializerOptions(), ObsJsonContext.Default.String);

    [Fact]
    public void ManagedFiles_KeepTheirBytes()
    {
        JsonParity.AssertSame(Indented, ObsManagedFiles.Json.ObsApplyUndo);
        JsonParity.AssertSame(Indented, ObsManagedFiles.Json.DictionaryStringObsManagedCollection);
        JsonParity.AssertSame(Indented, ObsManagedFiles.Json.ObsReleaseRollback);
        JsonParity.AssertSame(Indented, ObsManagedFiles.Json.ObsPendingRestore);
    }

    [Fact]
    public void RecordingClaim_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            },
            RecordingClaimStore.JsonOptions.RecordingClaim
        );
}
