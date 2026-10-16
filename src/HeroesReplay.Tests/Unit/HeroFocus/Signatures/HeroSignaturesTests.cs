using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Signatures;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Signatures;

/// <summary>The embedded hero signatures (#433): every hero, every name real.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroSignaturesTests
{
    private static readonly string[] Roles =
    {
        "Tank",
        "Bruiser",
        "Melee Assassin",
        "Ranged Assassin",
        "Healer",
        "Support",
    };

    private readonly HeroSignatures signatures = HeroSignatures.Embedded();

    [Fact]
    public void EveryRole_HasDefaults()
    {
        foreach (string role in Roles)
        {
            Assert.True(signatures.Roles.TryGetValue(role, out RoleSignature defaults), role);
            Assert.NotEmpty(defaults.Stats);
            Assert.NotEmpty(defaults.Awards);
        }
    }

    [Fact]
    public void EveryHeroInTheBuildReference_HasAnEntryWithAReason()
    {
        IReadOnlyList<HeroGuide> guides = GuideCatalog.Embedded().Reference.Heroes;

        Assert.True(guides.Count >= 90, $"{guides.Count} heroes in the build reference");
        foreach (HeroGuide guide in guides)
        {
            HeroSignatureEntry entry = signatures.Find(guide.UnitId, guide.Hero);
            Assert.True(entry != null, $"{guide.Hero} ({guide.UnitId}) has no signature entry.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Why), $"{guide.Hero} has no reason.");
            Assert.NotNull(signatures.For(guide.UnitId, guide.Hero, "Bruiser"));
        }

        Assert.Equal(
            signatures.Heroes.Count,
            signatures.Heroes.Select(entry => entry.UnitId).Distinct().Count()
        );
    }

    [Fact]
    public void EveryStatAndAward_IsOneTheParserHas()
    {
        var problems = new List<string>();
        foreach (KeyValuePair<string, RoleSignature> role in signatures.Roles)
        {
            Check(role.Key, role.Value.Stats, role.Value.Low, role.Value.Awards, problems);
        }

        foreach (HeroSignatureEntry entry in signatures.Heroes)
        {
            Check(entry.Hero, entry.Stats, entry.Low, entry.Awards, problems);
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void BuildRoles_NameARoleAndABuildOfThatHero()
    {
        GuideCatalog guides = GuideCatalog.Embedded();
        foreach (HeroSignatureEntry entry in signatures.Heroes.Where(e => e.BuildRoles != null))
        {
            HeroGuide guide = guides.Reference.Heroes.Single(g => g.UnitId == entry.UnitId);
            foreach (KeyValuePair<string, string> pair in entry.BuildRoles)
            {
                Assert.Contains(pair.Value, Roles);
                Assert.Contains(guide.Builds, build => build.Name == pair.Key);
            }
        }
    }

    [Fact]
    public void Varian_IsJudgedAsATankOnlyWithATauntBuild()
    {
        HeroSignature taunt = signatures.For(
            "HeroVarian",
            "Varian",
            "Bruiser",
            "Offensive Taunt Build"
        );
        HeroSignature blades = signatures.For(
            "HeroVarian",
            "Varian",
            "Bruiser",
            "Twin Blades Build"
        );

        Assert.Equal("Tank", taunt.Role);
        Assert.Equal(signatures.Roles["Tank"].Stats, taunt.Stats);
        Assert.Equal("Bruiser", blades.Role);
        Assert.Equal(signatures.Roles["Bruiser"].Stats, blades.Stats);
    }

    [Fact]
    public void Murky_DeathsDoNotCount_AndAHeroEntryOverridesItsRole()
    {
        HeroSignature murky = signatures.For("HeroMurky", "Murky", "Melee Assassin");
        HeroSignature dehaka = signatures.For("HeroDehaka", "Dehaka", "Bruiser");

        Assert.True(murky.IgnoreDeaths);
        Assert.Empty(murky.Low);
        Assert.Contains("MercCampCaptures", dehaka.Stats);
        Assert.Equal(signatures.Roles["Bruiser"].Low, dehaka.Low);
        Assert.Contains("MostMercCampsCaptured", dehaka.Awards);
    }

    [Fact]
    public void AHeroWithoutAnEntry_TakesItsRoleDefaults_AndNoRoleIsNull()
    {
        HeroSignature newcomer = signatures.For("HeroNewcomer", "Newcomer", "Healer");

        Assert.Equal(signatures.Roles["Healer"].Stats, newcomer.Stats);
        Assert.Null(signatures.For("HeroNewcomer", "Newcomer", null));
        Assert.Null(signatures.For(null));
    }

    private static void Check(
        string owner,
        IReadOnlyList<string> stats,
        IReadOnlyList<string> low,
        IReadOnlyList<string> awards,
        List<string> problems
    )
    {
        foreach (
            string stat in (stats ?? Array.Empty<string>()).Concat(low ?? Array.Empty<string>())
        )
        {
            if (!ScoreStats.IsStat(stat))
            {
                problems.Add($"{owner}: {stat} is not a ScoreResult stat.");
            }
        }

        foreach (string award in awards ?? Array.Empty<string>())
        {
            if (!ScoreStats.IsAward(award))
            {
                problems.Add($"{owner}: {award} is not a MatchAwardType.");
            }
        }
    }
}
