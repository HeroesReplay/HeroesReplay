using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Requests;

/// <summary>Source-generated JSON for the request queue and its sidecars (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(List<RewardQueueItem>))]
[JsonSerializable(typeof(RewardQueueItem))]
internal sealed partial class RequestsJsonContext : JsonSerializerContext;
