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
