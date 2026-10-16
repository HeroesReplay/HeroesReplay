using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Scoring;

/// <summary>The focus scorer's gates, its quality and novelty, and its order (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusScorerTests
{
    private static readonly HeroSignatures Signatures = HeroSignatures.Embedded();

    [Fact]
    public void AnAiOrAHeroWithoutARole_IsRuledOut()
    {
        FocusCandidate ai = Star(
            Card(1, Player(0, "Li-Ming", "Wiza", "Ranged Assassin", ai: true))
        );
        FocusCandidate unknown = Star(Card(2, Player(0, "Mystery", "Myst", null)));

        Assert.Equal(FocusGates.NotHuman, ai.Gate);
        Assert.Equal(FocusGates.NoHero, unknown.Gate);
    }

    [Fact]
    public void OnlyStormLeague_UnlessAnyModeIsAsked()
    {
        FocusCard quickMatch = Card(1, Strong("Li-Ming", "Wiza"), mode: "QuickMatch");

        Assert.Equal(FocusGates.NotStormLeague, Star(quickMatch).Gate);
        Assert.Null(Star(quickMatch, stormLeagueOnly: false).Gate);
    }

    [Fact]
    public void AHeroFocusedInTheLastThreePicks_WaitsItsTurn()
    {
        FocusCard card = Card(1, Strong("Li-Ming", "Wiza"));
        var recent = Ledger(
            Entry("Wiza", "Ranged Assassin", Now.AddHours(-4)),
            Entry("Muradin", "Tank", Now.AddHours(-3)),
            Entry("Uther", "Healer", Now.AddHours(-2))
        );
        var older = Ledger(
            Entry("Wiza", "Ranged Assassin", Now.AddHours(-5), map: "Sky Temple"),
            Entry("Muradin", "Tank", Now.AddHours(-3)),
            Entry("Uther", "Healer", Now.AddHours(-2)),
            Entry("Dehaka", "Bruiser", Now.AddHours(-1))
        );

        Assert.Equal(FocusGates.HeroRepeat, Star(card, recent).Gate);
        Assert.Null(Star(card, older).Gate);
    }

    [Fact]
    public void TheSameHeroBuildAndMap_IsADuplicate_ButAnotherMapIsNot()
    {
        FocusLedger ledger = Ledger(
            Entry("Wiza", "Ranged Assassin", Now.AddDays(-20)),
            Entry("Muradin", "Tank", Now.AddHours(-3)),
            Entry("Uther", "Healer", Now.AddHours(-2)),
            Entry("Dehaka", "Bruiser", Now.AddHours(-1))
        );

        Assert.Equal(FocusGates.Duplicate, Star(Card(1, Strong("Li-Ming", "Wiza")), ledger).Gate);
        Assert.Null(Star(Card(2, Strong("Li-Ming", "Wiza"), map: "Sky Temple"), ledger).Gate);
    }

    [Fact]
    public void ThePlayersRecordOnTheHero_MustBeStrongEnough()
    {
        FocusCandidate few = Star(
            Card(1, Strong("Li-Ming", "Wiza", FocusFixtures.Strength(9, 10)))
        );
        FocusCandidate even = Star(
            Card(2, Strong("Li-Ming", "Wiza", FocusFixtures.Strength(15, 30)))
        );
        FocusCandidate strong = Star(
            Card(3, Strong("Li-Ming", "Wiza", FocusFixtures.Strength(80, 120)))
        );
        FocusCandidate unknown = Star(Card(4, Strong("Li-Ming", "Wiza")));
        FocusCandidate unknownRefused = Star(
            Card(5, Strong("Li-Ming", "Wiza")),
            settings: new HeroFocusSettings { AllowUnknownStrength = false }
        );

        Assert.Equal(FocusGates.StrengthGames, few.Gate);
        Assert.Equal(FocusGates.StrengthWinRate, even.Gate);
        Assert.Null(strong.Gate);
        Assert.Null(unknown.Gate);
        Assert.True(strong.Parts["strength"] > unknown.Parts["strength"]);
        Assert.Equal(new HeroFocusSettings().UnknownStrength, unknown.Parts["strength"]);
        Assert.Equal(FocusGates.StrengthUnknown, unknownRefused.Gate);
    }

    [Fact]
    public void ThePlayerFocusedThisWeek_WaitsOutTheCooldown()
    {
        FocusCard card = Card(1, Strong("Li-Ming", "Wiza", name: "Star", battleTag: 1234));
        FocusLedger recently = Ledger(
            Entry("Muradin", "Tank", Now.AddDays(-2), player: "star#1234")
        );
        FocusLedger longAgo = Ledger(
            Entry("Muradin", "Tank", Now.AddDays(-8), player: "Star#1234")
        );

        Assert.Equal(FocusGates.PlayerCooldown, Star(card, recently).Gate);
        Assert.Null(Star(card, longAgo).Gate);
    }

    [Fact]
    public void APoorGame_IsUnderTheQualityFloor_HoweverNewItsHero()
    {
        FocusCardPlayer feeder = Player(
            0,
            "Li-Ming",
            "Wiza",
            "Ranged Assassin",
            kills: 0,
            deaths: 11,
            assists: 1,
            heroDamage: 5000,
            build: null
        );
        FocusCard card = Card(1, feeder, lengthSeconds: 500);
        card.Players[0].Won = false;

        FocusCandidate candidate = Star(card);

        Assert.Equal(FocusGates.QualityFloor, candidate.Gate);
        Assert.True(candidate.Novelty > 0.85);
        Assert.True(candidate.Quality < new HeroFocusSettings().QualityFloor);
    }

    [Fact]
    public void AReplayAlreadyPicked_IsNotPickedAgain()
    {
        FocusCard card = Card(1, Strong("Li-Ming", "Wiza"));
        FocusLedger ledger = Ledger(
            Entry("Muradin", "Tank", Now.AddHours(-1), state: FocusEntryState.Picked, key: "1")
        );

        Assert.Equal(FocusGates.ReplayUsed, Star(card, ledger).Gate);
    }

    [Fact]
    public void AnUncoveredHero_OutranksACoveredOne_AtEqualQuality()
    {
        FocusCard covered = Card(1, Strong("Valla", "Demo"));
        FocusCard uncovered = Card(2, Strong("Raynor", "Rayn"));
        FocusLedger ledger = Ledger(
            Entry("Demo", "Ranged Assassin", Now.AddDays(-3), map: "Sky Temple"),
            Entry("Muradin", "Tank", Now.AddHours(-3)),
            Entry("Uther", "Healer", Now.AddHours(-2)),
            Entry("Dehaka", "Bruiser", Now.AddHours(-1))
        );

        List<FocusCandidate> stars = Rank(new[] { covered, uncovered }, ledger)
            .Where(candidate => candidate.PlayerIndex == 0)
            .ToList();

        Assert.Equal("Rayn", stars[0].AttributeId);
        Assert.Equal(1, stars[0].Parts["novelty.hero"]);
        Assert.True(stars[1].Parts["novelty.hero"] < 0.5);
    }

    [Fact]
    public void APublishedFocus_CoversItsHeroMoreThanOneOnlyPlayed()
    {
        FocusCard card = Card(1, Strong("Valla", "Demo"));
        FocusLedger ledger = Ledger(
            Entry("Demo", "Ranged Assassin", Now.AddDays(-30), map: "Sky Temple", replayId: 77),
            Entry("Muradin", "Tank", Now.AddHours(-3)),
            Entry("Uther", "Healer", Now.AddHours(-2)),
            Entry("Dehaka", "Bruiser", Now.AddHours(-1))
        );

        double played = Star(card, ledger).Parts["novelty.hero"];
        double published = Star(card, ledger, published: new HashSet<int> { 77 }).Parts[
            "novelty.hero"
        ];

        Assert.Equal(1 / 1.5, played, 3);
        Assert.Equal(0.5, published, 3);
    }

    [Fact]
    public void ARoleUnderItsShare_IsMoreNovel_ThanOneOverIt()
    {
        FocusLedger ledger = Ledger(
            Entry("Demo", "Ranged Assassin", Now.AddHours(-6)),
            Entry("Rayn", "Ranged Assassin", Now.AddHours(-5)),
            Entry("Wiza", "Ranged Assassin", Now.AddHours(-4)),
            Entry("Hanz", "Ranged Assassin", Now.AddHours(-3))
        );

        FocusCandidate assassin = Star(Card(1, Strong("Tychus", "Tych")), ledger);
        FocusCandidate healer = Star(
            Card(2, Player(0, "Uther", "Uthe", "Healer", unitId: "HeroUther")),
            ledger
        );

        Assert.Equal(0, assassin.Parts["novelty.role"]);
        Assert.Equal(1, healer.Parts["novelty.role"]);
    }

    [Fact]
    public void TheScore_IsQualityTimesHalfPlusNovelty_AndTiesGoToTheNewerReplay()
    {
        FocusCard older = Card(100, Strong("Valla", "Demo"));
        FocusCard newer = Card(200, Strong("Valla", "Demo"));

        IReadOnlyList<FocusCandidate> ranked = Rank(new[] { older, newer }, new FocusLedger());

        FocusCandidate first = ranked[0];
        Assert.Equal(200, first.ReplayId);
        Assert.Equal(0, first.PlayerIndex);
        Assert.Equal(first.Quality * (0.5 + first.Novelty), first.Score, 3);
        Assert.Equal(100, ranked[1].ReplayId);
        Assert.Equal(ranked[0].Score, ranked[1].Score);
        Assert.All(ranked.SkipWhile(c => c.Eligible), c => Assert.False(c.Eligible));
    }

    [Fact]
    public void MurkysDeaths_DoNotLowerHisKda()
    {
        FocusCandidate murky = Star(
            Card(
                1,
                Player(
                    0,
                    "Murky",
                    "Murk",
                    "Melee Assassin",
                    kills: 5,
                    deaths: 12,
                    assists: 10,
                    unitId: "HeroMurky"
                )
            )
        );
        FocusCandidate valla = Star(
            Card(
                2,
                Player(0, "Valla", "Demo", "Ranged Assassin", kills: 5, deaths: 12, assists: 10)
            )
        );

        Assert.Equal(1, murky.Parts["kda"]);
        Assert.True(valla.Parts["kda"] < 0.2);
    }

    [Theory]
    [InlineData(null, 0.5)]
    [InlineData("EU", 1.0)]
    public void Deficit_WithNothingRecent_IsFullOrNeutral(string value, double expected) =>
        Assert.Equal(expected, FocusScorer.Deficit(value, Array.Empty<string>()));

    [Fact]
    public void Deficit_IsHalfAtItsShare_AndZeroAtTwiceIt()
    {
        string[] recent = { "EU", "NA", "EU", "NA" };

        Assert.Equal(0.5, FocusScorer.Deficit("EU", recent));
        Assert.Equal(1, FocusScorer.Deficit("KR", recent));
        Assert.Equal(0, FocusScorer.Deficit("EU", new[] { "EU", "EU" }, categories: 2));
    }

    [Fact]
    public void CompKey_IsSortedAndJoined()
    {
        Assert.Equal("Dive+Sustain", FocusScorer.CompKey(new[] { "Sustain", "Dive", "Dive" }));
        Assert.Equal("none", FocusScorer.CompKey(null));
    }

    [Theory]
    [InlineData(0.9, false, 1.0)]
    [InlineData(0.9, true, 0.6)]
    [InlineData(0.75, false, 0.6)]
    [InlineData(0.5, false, 0.2)]
    public void BuildClarity_RewardsAClearGuideBuild(
        double score,
        bool ambiguous,
        double expected
    ) =>
        Assert.Equal(
            expected,
            FocusScorer.BuildClarity(new FocusCardBuild { Score = score, Ambiguous = ambiguous })
        );

    private static FocusCardPlayer Strong(
        string hero,
        string attributeId,
        FocusStrength strength = null,
        string name = null,
        int battleTag = 0
    ) =>
        Player(
            0,
            hero,
            attributeId,
            "Ranged Assassin",
            strength: strength,
            name: name,
            battleTag: battleTag,
            awards: new[] { "MVP" }
        );

    private static FocusLedger Ledger(params FocusLedgerEntry[] entries) =>
        new() { Entries = entries.ToList() };

    private static IReadOnlyList<FocusCandidate> Rank(
        IEnumerable<FocusCard> cards,
        FocusLedger ledger,
        HeroFocusSettings settings = null,
        IReadOnlySet<int> published = null,
        bool stormLeagueOnly = true
    ) =>
        new FocusScorer(settings ?? new HeroFocusSettings(), Signatures).Rank(
            cards,
            ledger,
            new HeroBaselines(),
            Now,
            published,
            stormLeagueOnly
        );

    /// <summary>The scored star (player 0) of one card.</summary>
    private static FocusCandidate Star(
        FocusCard card,
        FocusLedger ledger = null,
        HeroFocusSettings settings = null,
        IReadOnlySet<int> published = null,
        bool stormLeagueOnly = true
    ) =>
        Rank(new[] { card }, ledger ?? new FocusLedger(), settings, published, stormLeagueOnly)
            .Single(candidate => candidate.PlayerIndex == 0);
}
