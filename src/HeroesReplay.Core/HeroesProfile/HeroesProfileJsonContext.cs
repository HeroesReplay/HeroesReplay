using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>Source-generated JSON for the HeroesProfile files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(HeroStatsSnapshot))]
[JsonSerializable(typeof(PlayerHeroStatsStore.StoredPlayer))]
[JsonSerializable(typeof(List<DateTimeOffset>))]
[JsonSerializable(typeof(PlayerPrivacyCursor))]
internal sealed partial class HeroesProfileJsonContext : JsonSerializerContext;
