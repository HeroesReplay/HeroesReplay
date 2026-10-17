using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Connectivity;

/// <summary>Source-generated JSON for the Connectivity files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(ReplayResumeFile.ReplayResumeRequest))]
internal sealed partial class ConnectivityJsonContext : JsonSerializerContext;
