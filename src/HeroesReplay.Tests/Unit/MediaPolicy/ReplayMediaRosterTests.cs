using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayMediaRosterTests
{
    private static readonly IReadOnlyList<Hero> Catalog = new[]
    {
        new Hero("Uther", "HeroUther", "Uther", "Uthe", role: "Healer"),
        new Hero("Nazeebo", "HeroWitchDoctor", "Nazeebo", "Witc", role: "Ranged Assassin"),
    };

    [Fact]
    public void Roster_UsesTheCatalogEnglishNameForALocalizedReplay()
    {
        LoadedReplay loaded = Loaded(
            Player("Uther le Porteur", "Uthe", team: 0),
            Player("Féticheur", "Witc", team: 1)
        );

        IReadOnlyList<ReplayMediaPlayer> roster = ReplayMediaFacts
            .From(loaded, false, false, false, Catalog)
            .Roster;

        Assert.Equal(new[] { "Uther", "Nazeebo" }, roster.Select(player => player.Hero));
    }

    [Fact]
    public void Roster_Aram_NamesTheHeroSpawnedNotTheLobbyHero()
    {
        // #348, replay 65773257: player 6 played Nazeebo, and the lobby attribute id was Zagara's.
        Player toadhog = Player("Nazeebo", "Zaga", team: 1);
        toadhog.HeroUnits = new List<Heroes.ReplayParser.Unit>
        {
            new() { Name = "HeroWitchDoctor", PlayerControlledBy = toadhog },
        };
        LoadedReplay loaded = Loaded(toadhog);
        loaded.Replay.GameMode = GameMode.ARAM;
        IReadOnlyList<Hero> catalog = Catalog
            .Append(new Hero("Zagara", "HeroZagara", "Zagara", "Zaga", role: "Ranged Assassin"))
            .ToList();

        IReadOnlyList<ReplayMediaPlayer> roster = ReplayMediaFacts
            .From(loaded, false, false, false, catalog)
            .Roster;

        Assert.Equal("Nazeebo", Assert.Single(roster).Hero);
    }

    [Fact]
    public void Roster_KeepsTheReplayNameWhenTheCatalogHasNoMatch()
    {
        LoadedReplay loaded = Loaded(Player("Héros inconnu", "Zzzz", team: 0));

        IReadOnlyList<ReplayMediaPlayer> roster = ReplayMediaFacts
            .From(loaded, false, false, false, Catalog)
            .Roster;

        Assert.Equal("Héros inconnu", Assert.Single(roster).Hero);
    }

    [Fact]
    public void Roster_WithoutACatalog_KeepsTheReplayName()
    {
        LoadedReplay loaded = Loaded(Player("Féticheur", "Witc", team: 1));

        IReadOnlyList<ReplayMediaPlayer> roster = ReplayMediaFacts
            .From(loaded, false, false, false)
            .Roster;

        Assert.Equal("Féticheur", Assert.Single(roster).Hero);
    }

    [Fact]
    public void Roster_KeepsTheNamedTalentsInPickOrder()
    {
        Player varian = Player("Varian", "Vari", team: 0);
        varian.Talents = new[]
        {
            new Talent { TalentID = 1, TalentName = "VarianParryOverpower" },
            new Talent { TalentID = 3 },
            new Talent { TalentID = 3, TalentName = "VarianTaunt" },
        };
        LoadedReplay loaded = Loaded(varian, Player("Uther", "Uthe", team: 1));

        IReadOnlyList<ReplayMediaPlayer> roster = ReplayMediaFacts
            .From(loaded, false, false, false, Catalog)
            .Roster;

        Assert.Equal(new[] { "VarianParryOverpower", "VarianTaunt" }, roster[0].Talents);
        Assert.Empty(roster[1].Talents);
    }

    private static LoadedReplay Loaded(params Player[] players) =>
        new()
        {
            Replay = new Replay
            {
                Map = "Dragon Shire",
                GameMode = GameMode.StormLeague,
                Players = players,
            },
        };

    private static Player Player(string character, string attributeId, int team) =>
        new()
        {
            Name = "Player" + team,
            Character = character,
            HeroAttributeId = attributeId,
            Team = team,
            PlayerType = PlayerType.Human,
        };
}
