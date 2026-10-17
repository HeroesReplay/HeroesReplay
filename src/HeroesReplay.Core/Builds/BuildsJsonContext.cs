using System.Text.Json.Serialization;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Builds.Reference;

namespace HeroesReplay.Core.Builds;

/// <summary>
/// Source-generated JSON for the talent-build reference and the guide extract (#454): no
/// reflection, so Native AOT reads and writes them. A list with an initializer is <c>set</c>,
/// not <c>init</c>: source generation passes every <c>init</c> property to the object
/// initializer, so one the file leaves out (an empty list) would read back as null.
/// </summary>
[JsonSerializable(typeof(GuideReference))]
[JsonSerializable(typeof(GuideExtractHero[]))]
internal sealed partial class BuildsJsonContext : JsonSerializerContext;
