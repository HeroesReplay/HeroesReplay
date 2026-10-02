using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TeamCompositionTests
{
    /// <summary>Roles, isMelee, ratings, and playstyles as heroes-data2 build 98304 has them.</summary>
    internal static readonly IReadOnlyList<Hero> Catalog = new[]
    {
        Hero("Johanna", HeroDraft.Tank, true, 3, 10, "BodyBlocker", "RoleTank", "WaveClearer"),
        Hero("Muradin", HeroDraft.Tank, true, 5, 9, "BodyBlocker", "Escaper", "Ganker"),
        Hero("E.T.C.", HeroDraft.Tank, true, 4, 9, "Escaper", "Ganker", "SelfHealer"),
        Hero("Cho", HeroDraft.Tank, true, 6, 10, "Escaper", "Ganker", "MercKiller", "SoloLaner"),
        Hero("Chen", HeroDraft.Bruiser, true, 5, 8, "Escaper", "Ganker", "SoloLaner"),
        Hero("Sonya", HeroDraft.Bruiser, true, 7, 6, "Ganker", "MercKiller", "SoloLaner"),
        Hero("Thrall", HeroDraft.Bruiser, true, 8, 7, "Escaper", "Ganker"),
        Hero(
            "Yrel",
            HeroDraft.Bruiser,
            true,
            5,
            10,
            "Escaper",
            "Ganker",
            "SelfHealer",
            "SoloLaner"
        ),
        Hero("Hogger", HeroDraft.Bruiser, true, 7, 7),
        Hero("Rehgar", HeroDraft.Healer, true, 5, 7, "AllyHealer", "Escaper", "SelfHealer"),
        Hero("Anduin", HeroDraft.Healer, false, 2, 4, "AllyHealer", "Helper", "SelfHealer"),
        Hero("Lt. Morales", HeroDraft.Healer, false, 3, 4, "AllyHealer", "Helper"),
        Hero(
            "Brightwing",
            HeroDraft.Healer,
            false,
            4,
            5,
            "AllyHealer",
            "Escaper",
            "SelfHealer",
            "SoloLaner"
        ),
        Hero("Valla", HeroDraft.RangedAssassin, false, 9, 4, "Ganker", "RoleCaster", "WaveClearer"),
        Hero("Raynor", HeroDraft.RangedAssassin, false, 9, 6, "TowerPusher"),
        Hero(
            "Jaina",
            HeroDraft.RangedAssassin,
            false,
            10,
            2,
            "RoleCaster",
            "TowerPusher",
            "WaveClearer"
        ),
        Hero(
            "Kael'thas",
            HeroDraft.RangedAssassin,
            false,
            9,
            3,
            "Ganker",
            "RoleCaster",
            "TowerPusher",
            "WaveClearer"
        ),
        Hero(
            "Li-Ming",
            HeroDraft.RangedAssassin,
            false,
            10,
            3,
            "Ganker",
            "RoleCaster",
            "TowerPusher"
        ),
        Hero("Nova", HeroDraft.RangedAssassin, false, 10, 2, "Ganker", "RoleCaster"),
        Hero("Chromie", HeroDraft.RangedAssassin, false, 9, 3, "RoleCaster"),
        Hero("Falstad", HeroDraft.RangedAssassin, false, 8, 4, "Escaper", "Ganker", "RoleCaster"),
        Hero("Genji", HeroDraft.RangedAssassin, false, 8, 4, "Escaper", "Ganker", "RoleCaster"),
        Hero("Sgt. Hammer", HeroDraft.RangedAssassin, false, 9, 3, "TowerPusher", "WaveClearer"),
        Hero(
            "Nazeebo",
            HeroDraft.RangedAssassin,
            false,
            8,
            3,
            "RoleSpecialist",
            "SoloLaner",
            "TowerPusher",
            "WaveClearer"
        ),
        Hero(
            "Zagara",
            HeroDraft.RangedAssassin,
            false,
            6,
            5,
            "RoleSpecialist",
            "SoloLaner",
            "TowerPusher",
            "WaveClearer"
        ),
        Hero("Illidan", HeroDraft.MeleeAssassin, true, 8, 6, "Escaper", "Ganker", "MercKiller"),
        Hero("Zeratul", HeroDraft.MeleeAssassin, true, 8, 6, "Escaper", "Ganker"),
        Hero(
            "The Butcher",
            HeroDraft.MeleeAssassin,
            true,
            9,
            5,
            "Ganker",
            "MercKiller",
            "SoloLaner"
        ),
        Hero("Maiev", HeroDraft.MeleeAssassin, true, 6, 7),
        Hero(
            "Abathur",
            HeroDraft.Support,
            true,
            3,
            1,
            "Ganker",
            "RoleSpecialist",
            "TowerPusher",
            "WaveClearer"
        ),
        new Hero(
            "The Lost Vikings",
            "HeroLostVikings",
            "LostVikings",
            "Lost",
            role: HeroDraft.Support,
            isMelee: true,
            ratings: new HeroRatings(7, 6, 5, 9)
        ),
    };

    [Fact]
    public void Labels_LeaveAStandardDraftAlone()
    {
        Assert.Empty(Labels("Johanna", "Chen", "Rehgar", "Valla", "Raynor"));
    }

    [Fact]
    public void SplitPush_NeedsThreeSoloLaners()
    {
        Assert.Equal(
            new[] { "Split push" },
            Labels("Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar")
        );
        Assert.DoesNotContain(
            "Split push",
            Labels("Johanna", "Chen", "Nazeebo", "Rehgar", "Valla")
        );
    }

    [Fact]
    public void SplitPush_DoesNotCountATowerPusherThatClearsWaves()
    {
        // Kael'thas and Jaina push and clear, but neither holds a lane alone.
        Assert.DoesNotContain(
            "Split push",
            Labels("Johanna", "Chen", "Kael'thas", "Jaina", "Rehgar")
        );
    }

    [Fact]
    public void Siege_NeedsFourPushersOrClearers()
    {
        Assert.Equal(
            new[] { "Siege" },
            Labels("Johanna", "Rehgar", "Jaina", "Kael'thas", "Sgt. Hammer")
        );
        Assert.DoesNotContain("Siege", Labels("Johanna", "Rehgar", "Jaina", "Kael'thas", "Nova"));
    }

    [Fact]
    public void Dive_NeedsFourGankersAndFourEscapers()
    {
        Assert.Equal(
            new[] { "Dive" },
            Labels("Muradin", "Thrall", "Falstad", "Genji", "Brightwing")
        );
        Assert.DoesNotContain(
            "Dive",
            Labels("Muradin", "Thrall", "Falstad", "Raynor", "Brightwing")
        );
    }

    [Fact]
    public void Poke_NeedsThreeRangedCasters()
    {
        Assert.Equal(new[] { "Poke" }, Labels("Johanna", "Anduin", "Jaina", "Chromie", "Li-Ming"));
        Assert.DoesNotContain("Poke", Labels("Johanna", "Anduin", "Jaina", "Chromie", "Raynor"));
    }

    [Fact]
    public void MeleeAssassins_AreCounted()
    {
        Assert.Equal(
            new[] { "Double melee assassin" },
            Labels("Johanna", "Illidan", "Zeratul", "Anduin", "Valla")
        );
        Assert.Contains(
            "Triple melee assassin",
            Labels("Johanna", "Illidan", "Zeratul", "The Butcher", "Anduin")
        );
    }

    [Fact]
    public void Melee_AllMeleeWinsOverOneRanged()
    {
        Assert.Equal(
            new[] { "All melee" },
            Labels("Johanna", "Thrall", "Illidan", "Zeratul", "Rehgar")
                .Where(label => !label.Contains("melee assassin"))
                .ToArray()
        );
        Assert.Equal(
            new[] { "One ranged" },
            Labels("Johanna", "Thrall", "Illidan", "Rehgar", "Valla")
        );
    }

    [Fact]
    public void Sustain_CountsTheHealerAndSelfHealers()
    {
        Assert.Equal(
            new[] { "Triple sustain" },
            Labels("E.T.C.", "Yrel", "Rehgar", "Valla", "Raynor")
        );
    }

    [Fact]
    public void Specialists_NeedThreeAndAreCounted()
    {
        Assert.Contains(
            "Triple specialist",
            Labels("Johanna", "Abathur", "Nazeebo", "Zagara", "Rehgar")
        );
        Assert.DoesNotContain(
            "Double specialist",
            Labels("Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar")
        );
    }

    [Fact]
    public void MercControl_NeedsTwoMercKillers()
    {
        Assert.Equal(
            new[] { "Merc control" },
            Labels("Johanna", "Sonya", "Anduin", "Illidan", "Valla")
        );
    }

    [Fact]
    public void GlassCannon_ReadsTheRatingAverages()
    {
        // Survivability (2 + 3 + 4 + 4 + 6) / 5 = 3.8, damage (10 + 10 + 9 + 3 + 8) / 5 = 8.
        Assert.Contains(
            "Glass cannon",
            Labels("Nova", "Li-Ming", "Valla", "Lt. Morales", "Zeratul")
        );
        Assert.DoesNotContain(
            "Glass cannon",
            Labels("Muradin", "Li-Ming", "Valla", "Lt. Morales", "Zeratul")
        );
    }

    [Fact]
    public void Labels_StayQuietForAnUnknownHeroAShortTeamOrNoCatalog()
    {
        Assert.Empty(Labels("Johanna", "Chen", "Nazeebo", "Zagara", "NotAHero"));
        Assert.Empty(Labels("Chen", "Nazeebo", "Zagara", "Rehgar"));
        Assert.Empty(
            TeamComposition.Labels(
                null,
                Team(0, "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar"),
                0
            )
        );
    }

    [Fact]
    public void Labels_FollowTheSwitchesAndTheWording()
    {
        var off = new YouTubeTitleSettings
        {
            Compositions = new TeamCompositionSettings { Enabled = false },
        };
        Assert.Empty(Labels(off, "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar"));

        var titles = new YouTubeTitleSettings();
        titles.Compositions.SplitPush.Label = "double soak";
        titles.Compositions.Siege.Enabled = false;
        Assert.Equal(
            new[] { "Double soak" },
            Labels(titles, "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar")
        );
        Assert.Empty(Labels(titles, "Johanna", "Rehgar", "Jaina", "Kael'thas", "Sgt. Hammer"));
    }

    [Fact]
    public void Labels_UseTheConfiguredThresholds()
    {
        var titles = new YouTubeTitleSettings();
        titles.Compositions.SplitPush.MinHeroes = 2;
        titles.Compositions.Dive.MinHeroes = 3;
        titles.Compositions.Dive.MinEscapers = 2;

        Assert.Contains(
            "Split push",
            Labels(titles, "Johanna", "Chen", "Nazeebo", "Rehgar", "Valla")
        );
        Assert.Contains("Dive", Labels(titles, "Muradin", "Thrall", "Valla", "Raynor", "Anduin"));
    }

    [Fact]
    public void Overrides_RemoveBrightwingAsASoloLaner()
    {
        string[] team = { "Johanna", "Chen", "Brightwing", "Nazeebo", "Valla" };

        Assert.DoesNotContain("Split push", Labels(team));
        Assert.Contains("Split push", Labels(NoOverrides(), team));
    }

    [Fact]
    public void Overrides_TagHeroesTheCatalogLeftEmpty()
    {
        string[] vikings = { "Johanna", "The Lost Vikings", "Nazeebo", "Zagara", "Rehgar" };
        Assert.Contains("Split push", Labels(vikings));
        Assert.Contains("Triple specialist", Labels(vikings));
        Assert.Empty(Labels(NoOverrides(), vikings));

        string[] hogger = { "Johanna", "Hogger", "Nazeebo", "Zagara", "Rehgar" };
        Assert.Contains("Split push", Labels(hogger));
        Assert.DoesNotContain("Split push", Labels(NoOverrides(), hogger));

        string[] maiev = { "Muradin", "Thrall", "Maiev", "Genji", "Rehgar" };
        Assert.Contains("Dive", Labels(maiev));
        Assert.DoesNotContain("Dive", Labels(NoOverrides(), maiev));
    }

    [Fact]
    public void Overrides_CountChoApartFromHisLaneAndAbathurAsNotMelee()
    {
        string[] cho = { "Cho", "Chen", "Nazeebo", "Rehgar", "Valla" };
        Assert.DoesNotContain("Split push", Labels(cho));
        Assert.Contains("Split push", Labels(NoOverrides(), cho));

        string[] abathur = { "Johanna", "Thrall", "Illidan", "Abathur", "Valla" };
        Assert.DoesNotContain("One ranged", Labels(abathur));
        Assert.Contains("One ranged", Labels(NoOverrides(), abathur));
    }

    [Fact]
    public void Overrides_FromConfigurationAddAndRemoveTags()
    {
        var titles = new YouTubeTitleSettings();
        titles.Compositions.HeroTagOverrides["valla"] = new HeroTagOverride
        {
            Add = new[] { TeamComposition.SoloLaner },
        };
        titles.Compositions.HeroTagOverrides["Nazeebo"] = new HeroTagOverride
        {
            Remove = new[] { TeamComposition.SoloLaner },
        };

        Hero valla = HeroDraft.Find(Catalog, "Valla");
        Assert.Contains(
            TeamComposition.SoloLaner,
            TeamComposition.Tags(valla, titles.Compositions)
        );
        Assert.Contains(
            "Split push",
            Labels(titles, "Johanna", "Chen", "Zagara", "Rehgar", "Valla")
        );
        Assert.DoesNotContain(
            "Split push",
            Labels(titles, "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar")
        );
    }

    [Fact]
    public void Unusual_DropsALabelAtOrAboveTheMaxFrequency()
    {
        var titles = new YouTubeTitleSettings();
        string[] team = { "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar" };
        Assert.Single(TeamComposition.Unusual(Catalog, Team(0, team), 0, titles));

        titles.Compositions.Frequencies[TeamComposition.SplitPush] = 0.10;
        Assert.Empty(TeamComposition.Unusual(Catalog, Team(0, team), 0, titles));
        Assert.Single(TeamComposition.Labels(Catalog, Team(0, team), 0, titles));

        titles.Compositions.MaxFrequency = 0.2;
        Assert.Single(TeamComposition.Unusual(Catalog, Team(0, team), 0, titles));
    }

    [Fact]
    public void CorpusFrequencies_KeepEveryDefaultLabelBelowTheMaxFrequency()
    {
        var settings = new TeamCompositionSettings();
        foreach (string key in TeamComposition.Keys)
        {
            Assert.True(settings.Frequency(key).HasValue, key);
            Assert.True(settings.IsUnusual(key), key);
        }
    }

    [Fact]
    public void Labels_ReadOneTeamOnly()
    {
        var roster = new List<ReplayMediaPlayer>();
        roster.AddRange(Team(0, "Johanna", "Chen", "Nazeebo", "Zagara", "Rehgar"));
        roster.AddRange(Team(1, "Johanna", "Chen", "Rehgar", "Valla", "Raynor"));

        Assert.Single(TeamComposition.Labels(Catalog, roster, 0));
        Assert.Empty(TeamComposition.Labels(Catalog, roster, 1));
    }

    internal static Hero Hero(
        string name,
        string role,
        bool melee,
        double damage,
        double survivability,
        params string[] playstyles
    )
    {
        string id = HeroDraft.Key(name);
        return new Hero(
            name,
            "Hero" + id,
            id,
            id,
            playstyles,
            role,
            isMelee: melee,
            ratings: new HeroRatings(damage, survivability, 5, 5)
        );
    }

    internal static List<ReplayMediaPlayer> Team(int team, params string[] heroes)
    {
        var players = new List<ReplayMediaPlayer>();
        foreach (string hero in heroes)
        {
            players.Add(new ReplayMediaPlayer { Team = team, Hero = hero });
        }

        return players;
    }

    private static string[] Labels(params string[] heroes) =>
        Labels(new YouTubeTitleSettings(), heroes);

    private static string[] Labels(YouTubeTitleSettings titles, params string[] heroes) =>
        TeamComposition
            .Labels(Catalog, Team(0, heroes), 0, titles)
            .Select(label => label.Text)
            .ToArray();

    private static YouTubeTitleSettings NoOverrides()
    {
        var titles = new YouTubeTitleSettings();
        titles.Compositions.HeroTagOverrides.Clear();
        return titles;
    }
}
