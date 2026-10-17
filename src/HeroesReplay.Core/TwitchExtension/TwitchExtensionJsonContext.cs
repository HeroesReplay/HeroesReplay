using System.Text.Json.Serialization;

namespace HeroesReplay.Core.TwitchExtension;

/// <summary>Source-generated JSON for the Heroes Profile Twitch extension bodies (#454): no reflection, so Native AOT sends and reads them.</summary>
[JsonSerializable(typeof(ExtensionSnapshot))]
[JsonSerializable(typeof(TwitchExtensionService.SnapshotBody))]
[JsonSerializable(typeof(TwitchExtensionService.WhoAmIBody))]
internal sealed partial class TwitchExtensionJsonContext : JsonSerializerContext;
