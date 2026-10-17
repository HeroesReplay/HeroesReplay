using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Dependencies;

/// <summary>Source-generated JSON for the Dependencies files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(DependencyInstallRecord))]
internal sealed partial class DependenciesJsonContext : JsonSerializerContext;
