using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfileExtension;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ExtensionPayloadBuilderTests
{
    [Fact]
    public void CreatePayloads_DropsObserversAndTheEleventhPlayer()
    {
        Player[] players = new Player[12];
        players[0] = Human("Obs", 1, 1, team: 2, "Raynor", "Rayn");
        for (int i = 1; i <= 11; i++)
        {
            players[i] = Human("P" + i, 100 + i, 1, team: i % 2, "Muradin", "Mur");
        }

        ExtensionGame game = Build(Replay(players));

        Assert.Equal(10, game.Players.Count);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(i => "P" + i),
            game.Players.Select(p => p.Name)
        );
        Assert.DoesNotContain(game.Players, p => p.Name == "Obs" || p.Name == "P11");
    }

    [Fact]
    public void CreatePayloads_CopiesRegionOntoAiAndPrefersCharacterName()
    {
        ExtensionGame game = Build(
            Replay(
                Human("Sam", 100, 2, team: 0, "Abathur", "Abat"),
                Computer("Elite", region: 0, team: 1, character: null, attribute: "Lili")
            )
        );

        ExtensionPlayer sam = game.Players[0];
        ExtensionPlayer elite = game.Players[1];
        Assert.Equal("Abathur", sam.Hero);
        Assert.Equal("Abat", sam.HeroAttribute);
        Assert.False(sam.Ai);
        Assert.Equal(100, sam.BattleTag);
        Assert.Equal("Lili", elite.Hero);
        Assert.Equal(0, elite.BattleTag);
        Assert.Equal(2, elite.Region);
        Assert.True(elite.Ai);
    }

    [Fact]
    public void CreatePayloads_DropsAiWhenNoRegionIsKnown()
    {
        ExtensionGame game = Build(
            Replay(Computer("Elite", region: 0, team: 0, character: "Lili", attribute: "Lili"))
        );

        Assert.Empty(game.Players);
    }

    [Fact]
    public void CreatePayloads_KeepsTrackerOrderCapsTalentsAndSkipsBrokenEvents()
    {
        Player sam = Human("Sam", 100, 1, team: 0, "Abathur", "Abat");
        var events = new List<TrackerEvent> { new() { Data = new TrackerEventStructure() } };
        for (int i = 1; i <= 8; i++)
        {
            events.Add(Talent(TimeSpan.FromMinutes(i), "Talent" + i, playerIdOneBased: 1));
        }

        events.Insert(3, Talent(TimeSpan.FromMinutes(9), "Talent2", playerIdOneBased: 1));

        ExtensionGame game = Build(Replay(new[] { sam }, events));

        Assert.Equal(
            Enumerable.Range(1, 7).Select(i => "Talent" + i),
            game.Talents.Select(pick => pick.TalentName)
        );
        Assert.Equal(
            Enumerable.Range(1, 7).Select(i => TimeSpan.FromMinutes(i)),
            game.Talents.Select(pick => pick.Time)
        );
        Assert.All(game.Talents, pick => Assert.Equal(0, pick.PlayerIndex));
    }

    [Fact]
    public void CreatePayloads_TruncatesMapToTheServerLimit()
    {
        Replay replay = Replay(Human("Sam", 100, 1, team: 0, "Abathur", "Abat"));
        replay.Map = new string('m', 70);

        ExtensionGame game = Build(replay);

        Assert.Equal(64, game.Map.Length);
    }

    [Fact]
    public void CreatePayloads_ReturnsNullWhenDisabled()
    {
        ExtensionGame game = new ExtensionPayloadBuilder(
            NullLogger<ExtensionPayloadBuilder>.Instance,
            Settings(enabled: false)
        ).CreatePayloads(Replay(Human("Sam", 100, 1, team: 0, "Abathur", "Abat")));

        Assert.Null(game);
    }

    private static ExtensionGame Build(Replay replay)
    {
        return new ExtensionPayloadBuilder(
            NullLogger<ExtensionPayloadBuilder>.Instance,
            Settings(enabled: true)
        ).CreatePayloads(replay);
    }

    private static AppSettings Settings(bool enabled)
    {
        return new AppSettings
        {
            TwitchExtension = new HeroesProfileTwitchExtensionSettings { Enabled = enabled },
            TrackerEvents = new TrackerEventSettings { TalentChosen = "TalentChosen" },
        };
    }

    private static Replay Replay(params Player[] players)
    {
        return Replay(players, new List<TrackerEvent>());
    }

    private static Replay Replay(Player[] players, List<TrackerEvent> events)
    {
        return new Replay
        {
            GameMode = GameMode.StormLeague,
            Map = "Cursed Hollow",
            ReplayVersion = "2.55.17.98025",
            Players = players,
            TrackerEvents = events,
        };
    }

    private static Player Human(
        string name,
        int tag,
        int region,
        int team,
        string hero,
        string attribute
    )
    {
        return new Player
        {
            Name = name,
            BattleTag = tag,
            BattleNetRegionId = region,
            Team = team,
            Character = hero,
            HeroAttributeId = attribute,
            PlayerType = PlayerType.Human,
        };
    }

    private static Player Computer(
        string name,
        int region,
        int team,
        string character,
        string attribute
    )
    {
        return new Player
        {
            Name = name,
            BattleTag = 55,
            BattleNetRegionId = region,
            Team = team,
            Character = character,
            HeroAttributeId = attribute,
            PlayerType = PlayerType.Computer,
        };
    }

    private static TrackerEvent Talent(TimeSpan time, string talent, int playerIdOneBased)
    {
        return new TrackerEvent
        {
            TimeSpan = time,
            TrackerEventType = ReplayTrackerEvents.TrackerEventType.StatGameEvent,
            Data = new TrackerEventStructure
            {
                dictionary = new Dictionary<int, TrackerEventStructure>
                {
                    [0] = Blob("TalentChosen"),
                    [1] = Optional(Blob(talent)),
                    [2] = Optional(new TrackerEventStructure { vInt = playerIdOneBased }),
                },
            },
        };
    }

    private static TrackerEventStructure Optional(TrackerEventStructure value)
    {
        return new TrackerEventStructure
        {
            optionalData = new TrackerEventStructure
            {
                array = new[]
                {
                    new TrackerEventStructure
                    {
                        dictionary = new Dictionary<int, TrackerEventStructure> { [1] = value },
                    },
                },
            },
        };
    }

    private static TrackerEventStructure Blob(string text)
    {
        return new TrackerEventStructure { blob = Encoding.UTF8.GetBytes(text) };
    }
}
