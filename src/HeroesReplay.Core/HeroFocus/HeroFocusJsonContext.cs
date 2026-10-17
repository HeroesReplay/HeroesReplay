using System.Text.Json.Serialization;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.HeroFocus.Targeted;

namespace HeroesReplay.Core.HeroFocus;

/// <summary>Source-generated JSON for the HeroFocus files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(FocusCard))]
[JsonSerializable(typeof(FocusLedger))]
[JsonSerializable(typeof(HeroBaselines))]
[JsonSerializable(typeof(HeroSignatureFile))]
[JsonSerializable(typeof(FocusTargetedState))]
internal sealed partial class HeroFocusJsonContext : JsonSerializerContext;
