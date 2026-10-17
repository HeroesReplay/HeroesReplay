using System.Text.Json;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus;

/// <summary>The HeroFocus files keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroFocusJsonTests
{
    /// <summary>The options of the cards, the ledger and <c>targeted.json</c> before #454.</summary>
    private static readonly JsonSerializerOptions StoreBefore = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Cards_KeepTheirBytes() =>
        JsonParity.AssertSame(StoreBefore, FocusCardStore.Json.FocusCard);

    [Fact]
    public void Ledger_KeepsItsBytes() =>
        JsonParity.AssertSame(StoreBefore, FocusLedgerStore.Json.FocusLedger);

    [Fact]
    public void Targeted_KeepsItsBytes() =>
        JsonParity.AssertSame(StoreBefore, FocusTargetedStore.Json.FocusTargetedState);

    [Fact]
    public void Baselines_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            },
            HeroBaselineStore.Json.HeroBaselines
        );

    [Fact]
    public void Signatures_ReadTheSame() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            },
            HeroSignatures.Json.HeroSignatureFile
        );
}
