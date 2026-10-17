using System.Text.Json.Serialization;
using HeroesReplay.Core.Spectating.Control;

namespace HeroesReplay.Core.Spectating;

/// <summary>Source-generated JSON for the Spectating files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(ObserverPanelRequests.StoredPanels))]
internal sealed partial class SpectatingJsonContext : JsonSerializerContext;
