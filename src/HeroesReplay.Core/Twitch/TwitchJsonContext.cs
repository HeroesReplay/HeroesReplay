using System.Collections.Generic;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.Rewards;

namespace HeroesReplay.Core.Twitch;

/// <summary>
/// Source-generated JSON for the Twitch files and the EventSub subscribe body (#454): no
/// reflection, so Native AOT reads and writes them. <see cref="PredictionReport"/>'s lists are
/// <c>set</c>, not <c>init</c>: source generation passes every <c>init</c> property to the
/// object initializer, so a list an older saved report lacks would read back as null.
/// </summary>
[JsonSerializable(typeof(PredictionLedgerDocument))]
[JsonSerializable(typeof(PredictionReport))]
[JsonSerializable(typeof(PredictionStreakBook))]
[JsonSerializable(typeof(IEnumerable<SupportedReward>))]
[JsonSerializable(typeof(EventSubSubscribeBody))]
internal sealed partial class TwitchJsonContext : JsonSerializerContext;
