using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.HeroesData;

/// <summary>
/// #348: in ARAM the lobby hero attribute and hero id are the hero the player had selected
/// before the match, not the hero the game assigned. The fixture holds the hero fields of the two
/// ARAM replays from the issue (65745237, 65773257) and of a Storm League and a Quick Match replay,
/// captured on 2.57.0.98348, with the heroes-data2 entries they name.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class PlayedHeroTests
{
    private static readonly Lazy<Fixture> Captured = new(Load);

    public static TheoryData<int> CapturedReplays() =>
        new() { 65745237, 65773257, 65822779, 65823384 };

    [Fact]
    public void Name_AramReplaysFromTheIssue_AreTheHeroesPlayedNotTheLobbyHeroes()
    {
        IReadOnlyList<Hero> catalog = Captured.Value.Catalog;
        (Replay valla, Player neko) = Parsed(65745237, playerIndex: 3, units: true);
        (Replay nazeebo, Player toadhog) = Parsed(65773257, playerIndex: 6, units: true);

        Assert.Equal("Thrall", HeroDraft.Find(catalog, neko.HeroAttributeId).Name);
        Assert.Equal("Zagara", HeroDraft.Find(catalog, toadhog.HeroAttributeId).Name);
        Assert.Equal("Valla", PlayedHero.Name(catalog, valla, neko));
        Assert.Equal("Nazeebo", PlayedHero.Name(catalog, nazeebo, toadhog));
        Assert.Equal("Demo", PlayedHero.AttributeId(catalog, valla, neko));
        Assert.Equal("Witc", PlayedHero.AttributeId(catalog, nazeebo, toadhog));
    }

    [Theory]
    [MemberData(nameof(CapturedReplays))]
    public void Name_FromHeroUnits_IsTheReplaysOwnEnglishCharacterName(int replayId)
    {
        Replay replay = Build(replayId, units: true, spawnEvents: false);

        Assert.Equal(EnglishNames(replay), OnlySpawnedNames(replay));
    }

    [Theory]
    [MemberData(nameof(CapturedReplays))]
    public void Name_FromPlayerSpawnedEvents_WhenUnitsWereNotParsed(int replayId)
    {
        Replay replay = Build(replayId, units: false, spawnEvents: true);

        Assert.Equal(EnglishNames(replay), OnlySpawnedNames(replay));
    }

    [Theory]
    [InlineData(65822779)]
    [InlineData(65823384)]
    public void Find_StormLeagueAndQuickMatch_AgreeWithTheLobbyHero(int replayId)
    {
        IReadOnlyList<Hero> catalog = Captured.Value.Catalog;
        Replay replay = Build(replayId, units: true, spawnEvents: true);

        Assert.All(
            replay.Players,
            player =>
                Assert.Same(
                    HeroDraft.Find(catalog, player.HeroAttributeId),
                    PlayedHero.Find(catalog, replay, player)
                )
        );
    }

    [Fact]
    public void Find_AramWithoutUnitsOrEvents_SkipsTheLobbyHero()
    {
        IReadOnlyList<Hero> catalog = Captured.Value.Catalog;
        Replay replay = Build(65745237, units: false, spawnEvents: false);
        Player neko = replay.Players[3];

        Assert.Equal("Valla", PlayedHero.Name(catalog, replay, neko));

        neko.Character = "Валла";
        Assert.Null(PlayedHero.Find(catalog, replay, neko));
        Assert.Equal("Валла", PlayedHero.Name(catalog, replay, neko));
        Assert.Null(PlayedHero.AttributeId(catalog, replay, neko));
    }

    [Fact]
    public void Find_OutsideAramWithoutUnits_KeepsTheLobbyHeroForALocalizedReplay()
    {
        IReadOnlyList<Hero> catalog = Captured.Value.Catalog;
        Replay replay = Build(65822779, units: false, spawnEvents: false);
        Player valla = replay.Players[7];
        valla.Character = "Валла";

        Assert.Equal("Valla", PlayedHero.Name(catalog, replay, valla));
        Assert.Equal("Demo", PlayedHero.AttributeId(catalog, replay, valla));
    }

    [Theory]
    [InlineData("HeroDVaPilot", "D.Va")]
    [InlineData("HeroBaleog", "The Lost Vikings")]
    public void Find_AHerosOtherUnit_IsThatHero(string unit, string hero)
    {
        var player = new Player
        {
            Character = "?",
            HeroAttributeId = "Thra",
            HeroUnits = new List<ReplayUnit>(),
        };
        player.HeroUnits.Add(new ReplayUnit { Name = unit, PlayerControlledBy = player });
        var replay = new Replay { GameMode = GameMode.ARAM, Players = new[] { player } };

        Assert.Equal(hero, PlayedHero.Name(Captured.Value.Catalog, replay, player));
    }

    [Fact]
    public void Find_TakesTheFirstHeroUnitBornNotALaterOne()
    {
        var player = new Player
        {
            Character = "Abathur",
            HeroAttributeId = "Abat",
            HeroUnits = new List<ReplayUnit>(),
        };
        player.HeroUnits.Add(
            new ReplayUnit
            {
                Name = "HeroDemonHunter",
                TimeSpanBorn = TimeSpan.FromMinutes(12),
                PlayerControlledBy = player,
            }
        );
        player.HeroUnits.Add(
            new ReplayUnit
            {
                Name = "HeroAbathur",
                TimeSpanBorn = TimeSpan.FromSeconds(40),
                PlayerControlledBy = player,
            }
        );
        var replay = new Replay { GameMode = GameMode.ARAM, Players = new[] { player } };

        Assert.Equal("Abathur", PlayedHero.Name(Captured.Value.Catalog, replay, player));
    }

    /// <summary>The replay's own character names: English in these four replays.</summary>
    private static string[] EnglishNames(Replay replay) =>
        replay.Players.Select(player => player.Character).ToArray();

    /// <summary>
    /// Each player's name with the character name and the lobby ids removed, so only the spawned
    /// hero unit can name the hero.
    /// </summary>
    private static string[] OnlySpawnedNames(Replay replay)
    {
        foreach (Player player in replay.Players)
        {
            player.Character = null;
            player.HeroId = null;
            player.HeroAttributeId = null;
        }

        return replay
            .Players.Select(player => PlayedHero.Name(Captured.Value.Catalog, replay, player))
            .ToArray();
    }

    private static (Replay Replay, Player Player) Parsed(int replayId, int playerIndex, bool units)
    {
        Replay replay = Build(replayId, units, spawnEvents: false);
        return (replay, replay.Players[playerIndex]);
    }

    /// <summary>
    /// The captured replay as the parser would give it: hero units when units were parsed, and
    /// the PlayerSpawned stat events when only events were.
    /// </summary>
    private static Replay Build(int replayId, bool units, bool spawnEvents)
    {
        CapturedReplay captured = Captured.Value.Replays.Single(item => item.ReplayId == replayId);
        var players = new Player[captured.Players.Count];
        var events = new List<TrackerEvent>();
        foreach (CapturedPlayer source in captured.Players)
        {
            var player = new Player
            {
                Team = source.Team,
                Character = source.Character,
                HeroId = source.HeroId,
                HeroAttributeId = source.HeroAttributeId,
                PlayerType = PlayerType.Human,
                HeroUnits = new List<ReplayUnit>(),
            };
            if (units)
            {
                foreach (string name in source.Units)
                {
                    player.HeroUnits.Add(
                        new ReplayUnit
                        {
                            Name = name,
                            TimeSpanBorn = TimeSpan.FromSeconds(40),
                            PlayerControlledBy = player,
                        }
                    );
                }
            }

            if (spawnEvents)
            {
                events.Add(Spawned(source.Spawned, source.Index + 1));
            }

            players[source.Index] = player;
        }

        return new Replay
        {
            GameMode = Enum.Parse<GameMode>(captured.GameMode),
            Map = captured.Map,
            Players = players,
            TrackerEvents = events,
        };
    }

    /// <summary><c>{"PlayerSpawned", [{{"Hero"}, unit}], [{{"PlayerID"}, id}]}</c>.</summary>
    private static TrackerEvent Spawned(string unit, int playerId)
    {
        return new TrackerEvent
        {
            TimeSpan = TimeSpan.FromSeconds(40),
            TrackerEventType = ReplayTrackerEvents.TrackerEventType.StatGameEvent,
            Data = new TrackerEventStructure
            {
                dictionary = new Dictionary<int, TrackerEventStructure>
                {
                    [0] = Blob("PlayerSpawned"),
                    [1] = Pair(Blob("Hero"), Blob(unit)),
                    [2] = Pair(Blob("PlayerID"), new TrackerEventStructure { vInt = playerId }),
                },
            },
        };
    }

    private static TrackerEventStructure Pair(
        TrackerEventStructure key,
        TrackerEventStructure value
    )
    {
        return new TrackerEventStructure
        {
            optionalData = new TrackerEventStructure
            {
                array = new[]
                {
                    new TrackerEventStructure
                    {
                        dictionary = new Dictionary<int, TrackerEventStructure>
                        {
                            [0] = new TrackerEventStructure
                            {
                                dictionary = new Dictionary<int, TrackerEventStructure>
                                {
                                    [0] = key,
                                },
                            },
                            [1] = value,
                        },
                    },
                },
            },
        };
    }

    private static TrackerEventStructure Blob(string text)
    {
        return new TrackerEventStructure { blob = Encoding.UTF8.GetBytes(text) };
    }

    private static Fixture Load()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "PlayedHeroes",
            "issue-348-heroes.json"
        );
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        var catalog = root.GetProperty("catalog")
            .EnumerateArray()
            .Select(hero => new Hero(
                hero.GetProperty("name").GetString(),
                hero.GetProperty("unitId").GetString(),
                hero.GetProperty("hyperlinkId").GetString(),
                hero.GetProperty("attributeId").GetString(),
                heroUnitIds: Strings(hero.GetProperty("heroUnitIds"))
            ))
            .ToList();
        var replays = root.GetProperty("replays")
            .EnumerateArray()
            .Select(replay => new CapturedReplay(
                replay.GetProperty("replayId").GetInt32(),
                replay.GetProperty("gameMode").GetString(),
                replay.GetProperty("map").GetString(),
                replay
                    .GetProperty("players")
                    .EnumerateArray()
                    .Select(player => new CapturedPlayer(
                        player.GetProperty("index").GetInt32(),
                        player.GetProperty("team").GetInt32(),
                        player.GetProperty("character").GetString(),
                        player.GetProperty("heroId").GetString(),
                        player.GetProperty("heroAttributeId").GetString(),
                        Strings(player.GetProperty("units")),
                        player.GetProperty("spawned").GetString()
                    ))
                    .ToList()
            ))
            .ToList();
        return new Fixture(catalog, replays);
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()).ToArray();

    private sealed record Fixture(
        IReadOnlyList<Hero> Catalog,
        IReadOnlyList<CapturedReplay> Replays
    );

    private sealed record CapturedReplay(
        int ReplayId,
        string GameMode,
        string Map,
        IReadOnlyList<CapturedPlayer> Players
    );

    private sealed record CapturedPlayer(
        int Index,
        int Team,
        string Character,
        string HeroId,
        string HeroAttributeId,
        IReadOnlyList<string> Units,
        string Spawned
    );
}
