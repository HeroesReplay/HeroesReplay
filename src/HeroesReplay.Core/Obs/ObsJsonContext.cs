using System.Collections.Generic;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Recording;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Source-generated JSON for the Obs files (#454): no reflection, so Native AOT reads and writes
/// them. <see cref="ObsBundleManifest.Assets"/> is <c>set</c>, not <c>init</c>: source
/// generation passes every <c>init</c> property to the object initializer, so a manifest without
/// assets would read back as null.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ObsBundleManifest))]
[JsonSerializable(typeof(ObsApplyUndo))]
[JsonSerializable(typeof(Dictionary<string, ObsManagedCollection>))]
[JsonSerializable(typeof(ObsReleaseRollback))]
[JsonSerializable(typeof(ObsPendingRestore))]
[JsonSerializable(typeof(RecordingClaim))]
internal sealed partial class ObsJsonContext : JsonSerializerContext;
