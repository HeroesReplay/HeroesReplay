using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroDraftTests
{
    private static readonly IReadOnlyList<Hero> Catalog = new[]
    {
        Hero("Johanna", HeroDraft.Tank),
        Hero("Muradin", HeroDraft.Tank),
        Hero("Chen", HeroDraft.Bruiser),
        Hero("Leoric", HeroDraft.Bruiser),
        Hero("Artanis", HeroDraft.Bruiser),
        Hero("Dehaka", HeroDraft.Bruiser),
        Hero("Sonya", HeroDraft.Bruiser),
        Hero("Rehgar", HeroDraft.Healer),
        Hero("Uther", HeroDraft.Healer),
        Hero("Valla", HeroDraft.RangedAssassin),
        Hero("Jaina", HeroDraft.RangedAssassin),
        Hero("LiMing", HeroDraft.RangedAssassin),
        Hero("Raynor", HeroDraft.RangedAssassin),
        Hero("Illidan", HeroDraft.MeleeAssassin),
        Hero("Medivh", HeroDraft.Support),
        Hero("Abathur", HeroDraft.Support),
        Hero("LostVikings", HeroDraft.Support),
        Hero("Zarya", HeroDraft.Support),
    };

    [Fact]
    public void Phrase_LeavesATankBruiserHealerAndCasterAlone()
    {
        Assert.Null(
            HeroDraft.Phrase(Catalog, Team(0, "Johanna", "Chen", "Rehgar", "Valla", "Jaina"))
        );
        Assert.Null(
            HeroDraft.Phrase(Catalog, Team(0, "Johanna", "Leoric", "Rehgar", "Valla", "Artanis"))
        );
    }

    [Fact]
    public void Phrase_NamesAMissingHealerAndAMissingTank()
    {
        Assert.Equal(
            "No healer",
            HeroDraft.Phrase(
                Catalog,
                Join(
                    Team(0, "Johanna", "Chen", "Raynor", "Valla", "Jaina"),
                    Team(1, "Muradin", "Dehaka", "Illidan", "Raynor", "Jaina")
                )
            )
        );
        Assert.Equal(
            "Blue no tank",
            HeroDraft.Phrase(Catalog, Team(0, "Chen", "Rehgar", "Raynor", "Valla", "Jaina"))
        );
    }

    [Fact]
    public void Phrase_NamesDoublesWithoutCallingABruiserASecondTank()
    {
        Assert.Equal(
            "Double tank",
            HeroDraft.Phrase(
                Catalog,
                Join(
                    Team(0, "Johanna", "Muradin", "Rehgar", "Valla", "Jaina"),
                    Team(1, "Johanna", "Muradin", "Rehgar", "Valla", "Jaina")
                )
            )
        );
        Assert.Equal(
            "Blue double bruiser",
            HeroDraft.Phrase(Catalog, Team(0, "Chen", "Dehaka", "Rehgar", "Valla", "Jaina"))
        );
        Assert.Equal(
            "Blue double healer",
            HeroDraft.Phrase(
                Catalog,
                Join(
                    Team(0, "Johanna", "Chen", "Rehgar", "Uther", "Jaina"),
                    Team(1, "Johanna", "Chen", "Rehgar", "Raynor", "Illidan")
                )
            )
        );
    }

    [Fact]
    public void Phrase_NamesSupportsAndMatchesPunctuatedHeroNames()
    {
        Assert.Equal(
            "Blue double support",
            HeroDraft.Phrase(
                Catalog,
                Team(0, "Johanna", "Rehgar", "Jaina", "Medivh", "The Lost Vikings")
            )
        );
        Assert.Equal(
            "Blue no healer",
            HeroDraft.Phrase(Catalog, Team(0, "Johanna", "Chen", "Li-Ming", "Valla", "Zarya"))
        );
    }

    [Fact]
    public void Phrase_StaysQuietWhenTheNoteIsTurnedOff()
    {
        var titles = new YouTubeTitleSettings { NoHealer = false, NoRangedAssassin = false };

        Assert.Null(
            HeroDraft.Phrase(
                Catalog,
                Team(0, "Johanna", "Chen", "Raynor", "Valla", "Jaina"),
                titles
            )
        );
        Assert.Null(
            HeroDraft.Phrase(
                Catalog,
                Team(0, "Johanna", "Chen", "Rehgar", "Valla", "Jaina"),
                titles
            )
        );
    }

    [Fact]
    public void Phrase_StaysQuietWhenAHeroIsUnknownOrTheTeamIsShort()
    {
        Assert.Null(
            HeroDraft.Phrase(Catalog, Team(0, "Johanna", "Chen", "NotAHero", "Valla", "Jaina"))
        );
        Assert.Null(HeroDraft.Phrase(Catalog, Team(0, "Johanna", "Chen", "Raynor")));
        Assert.Null(HeroDraft.Phrase(null, Team(0, "Johanna", "Chen", "Raynor", "Valla", "Jaina")));
    }

    [Fact]
    public void Title_KeepsTheMapAndAddsTheDraftNote()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65550001,
                Map = "Cursed Hollow",
                GameMode = "Storm League",
                Rank = "Diamond",
                Roster = Join(
                    Team(0, "Johanna", "Muradin", "Rehgar", "Valla", "Jaina"),
                    Team(1, "Johanna", "Muradin", "Rehgar", "Valla", "Jaina")
                ),
                HeroCatalog = Catalog,
            },
            null
        );

        Assert.Equal(
            "Cursed Hollow - Storm League - Diamond - Double tank - 65550001",
            metadata.Title
        );
        Assert.Contains("Draft: Double tank", metadata.DescriptionLines);
        Assert.DoesNotContain("MMR", metadata.Title, System.StringComparison.OrdinalIgnoreCase);
    }

    private static Hero Hero(string name, string role)
    {
        return new Hero(name, "Hero" + name, name, name, role: role);
    }

    private static List<ReplayMediaPlayer> Team(int team, params string[] heroes)
    {
        var players = new List<ReplayMediaPlayer>();
        foreach (string hero in heroes)
        {
            players.Add(new ReplayMediaPlayer { Team = team, Hero = hero });
        }

        return players;
    }

    private static List<ReplayMediaPlayer> Join(
        List<ReplayMediaPlayer> left,
        List<ReplayMediaPlayer> right
    )
    {
        left.AddRange(right);
        return left;
    }
}
