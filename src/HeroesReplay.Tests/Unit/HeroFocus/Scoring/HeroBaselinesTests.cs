using System;
using System.IO;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Scoring;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroBaselinesTests
{
    private readonly HeroSignatures signatures = HeroSignatures.Embedded();

    [Fact]
    public void Add_CountsEachCardOnce_PerHeroAndPerRole()
    {
        var baselines = new HeroBaselines();
        FocusCard card = FocusFixtures.Card(
            1,
            FocusFixtures.Player(0, "Li-Ming", "Wiza", "Ranged Assassin", unitId: "HeroWizard")
        );

        Assert.True(baselines.Add(card, signatures, 300));
        Assert.False(baselines.Add(card, signatures, 300));

        Assert.Equal(1, baselines.Samples("Wiza", "HeroDamage"));
        Assert.Equal(1, baselines.Samples("Fill1", "HeroDamage"));
        Assert.Equal(10, baselines.Roles["Ranged Assassin"]["HeroDamage"].Count);
    }

    [Fact]
    public void Percentile_UsesTheHero_ThenItsRole_AndTurnsLowStatsAround()
    {
        var baselines = new HeroBaselines();
        for (int i = 1; i <= 4; i++)
        {
            FocusCardPlayer valla = FocusFixtures.Player(
                0,
                "Valla",
                "Demo",
                "Ranged Assassin",
                heroDamage: i * 10000,
                deaths: i
            );
            baselines.Add(FocusFixtures.Card(i, valla), signatures, 300);
        }

        // Against Valla's four values (10k, 20k, 30k, 40k): 25k beats two.
        Assert.Equal(
            0.5,
            baselines.Percentile("Demo", "Ranged Assassin", "HeroDamage", 25000, false, 4)
        );
        // Deaths are a low stat: 1 death ties the best of four and beats the rest.
        Assert.Equal(
            1 - 0.5 / 4,
            baselines.Percentile("Demo", "Ranged Assassin", "Deaths", 1, true, 4)
        );
        // Too few Valla values: her role's (fillers too) decide.
        Assert.NotNull(
            baselines.Percentile("Demo", "Ranged Assassin", "HeroDamage", 25000, false, 5)
        );
        Assert.Null(
            baselines.Percentile("Demo", "Ranged Assassin", "HeroDamage", 25000, false, 500)
        );
        Assert.Null(baselines.Percentile("Nobody", "Healer", "Healing", 1, false, 1));
    }

    [Fact]
    public void Add_KeepsOnlyTheNewestValuesPerStat()
    {
        var baselines = new HeroBaselines();
        for (int i = 1; i <= 5; i++)
        {
            baselines.Add(
                FocusFixtures.Card(i, FocusFixtures.Player(0, "Valla", "Demo", "Ranged Assassin")),
                signatures,
                window: 3
            );
        }

        Assert.Equal(3, baselines.Samples("Demo", "HeroDamage"));
        Assert.Equal(5, baselines.Folded.Count);
    }

    [Fact]
    public void Store_RoundTrips_AndAnUnreadableFileReadsAsEmpty()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "hr-baselines-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var store = new HeroBaselineStore(Path.Combine(folder, "baselines.json"));
            var baselines = new HeroBaselines();
            baselines.Add(
                FocusFixtures.Card(1, FocusFixtures.Player(0, "Valla", "Demo", "Ranged Assassin")),
                signatures,
                300
            );

            store.Write(baselines);
            Assert.Equal(1, store.Read().Samples("Demo", "HeroDamage"));

            File.WriteAllText(store.FilePath, "{ broken");
            Assert.Equal(0, store.Read().Samples("Demo", "HeroDamage"));
            Assert.True(File.Exists(store.FilePath));
        }
        finally
        {
            TestTemp.Delete(folder);
        }
    }
}
