using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.GameClient;

/// <summary>Source-generated JSON for the GameClient files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(Dictionary<string, DateTimeOffset>))]
[JsonSerializable(typeof(SortedDictionary<string, DateTimeOffset>))]
internal sealed partial class GameClientJsonContext : JsonSerializerContext;
