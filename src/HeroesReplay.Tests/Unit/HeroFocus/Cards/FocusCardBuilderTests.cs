using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Builds;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

/// <summary>A parsed replay into a focus card (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusCardBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_ReadsEachPlayersCatalogHeroStatsAwardsAndBuild()
    {
        Hero liMing = RoledHero("Li-Ming", "HeroWizard", "Wiza", "Ranged Assassin");
        Replay replay = Replay(liMing);

        FocusCard card = Builder()
            .Build(
                replay,
                new[] { liMing },
                new FocusReplayFacts(
                    "65550003",
                    65550003,
                    "65550003.StormReplay",
                    "Diamond 3",
                    2810
                ),
                Now
            );

        Assert.Equal("65550003", card.Key);
        Assert.Equal(65550003, card.ReplayId);
        Assert.Equal("Alterac Pass", card.Map);
        Assert.Equal("StormLeague", card.GameMode);
        Assert.Equal("Diamond 3", card.Rank);
        Assert.Equal(2810, card.AverageMmr);
        Assert.Equal("EU", card.Region);
        Assert.Equal(0, card.WinnerTeam);
        Assert.Equal(1200, card.LengthSeconds);
        Assert.Equal("2026-10-12", card.GuideCheckedAt);
        Assert.Equal(10, card.Players.Count);

        FocusCardPlayer star = card.Players[0];
        Assert.Equal("Li-Ming", star.Hero);
        Assert.Equal("Wiza", star.AttributeId);
        Assert.Equal("Ranged Assassin", star.Role);
        Assert.Equal("Star#1234", star.Player);
        Assert.Equal(2, star.RegionId);
        Assert.True(star.Won);
        Assert.False(star.IsAi);
        Assert.Equal(9, star.Stat("SoloKills"));
        Assert.Equal(3, star.Stat("MercCampCaptures"));
        Assert.Contains("MVP", star.Awards);
        Assert.Equal("Teleport Build", star.Build.Name);
        Assert.Equal(1, star.Build.Score);
        Assert.False(star.Build.Ambiguous);
    }

    [Fact]
    public void Build_NamesTheEnglishCatalogHero_WhateverTheReplaysLanguage()
    {
        Hero liMing = RoledHero("Li-Ming", "HeroWizard", "Wiza", "Ranged Assassin");
        Replay replay = Replay(liMing);
        replay.Players[0].Character = "李敏";

        FocusCard card = Builder().Build(replay, new[] { liMing }, new FocusReplayFacts("x"), Now);

        Assert.Equal("Li-Ming", card.Players[0].Hero);
    }

    [Fact]
    public void Build_KeepsAiPlayersWithoutABuild_AndSwapsCompsBetweenTeams()
    {
        Hero liMing = RoledHero("Li-Ming", "HeroWizard", "Wiza", "Ranged Assassin");
        Replay replay = Replay(liMing);
        replay.Players[9].PlayerType = PlayerType.Computer;

        FocusCard card = Builder().Build(replay, new[] { liMing }, new FocusReplayFacts("x"), Now);

        FocusCardPlayer ai = card.Players[9];
        Assert.True(ai.IsAi);
        Assert.Null(ai.Build);
        Assert.Equal(card.Players[0].AllyComp, card.Players[9].EnemyComp);
        Assert.Equal(card.Players[0].EnemyComp, card.Players[9].AllyComp);
    }

    [Fact]
    public void Build_KeepsAnIndividualPentakill_AsTheReplaysNotableEvent()
    {
        // Player 0 (Hero 0) lands the killing blow on all five of team 1, 4 s apart (#510).
        Replay replay = FocusReplays.Replay();
        Player killer = replay.Players[0];
        ReplayUnit killerUnit = killer.HeroUnits[0];
        for (int victim = 5; victim < 10; victim++)
        {
            ReplayUnit unit = replay.Players[victim].HeroUnits[0];
            unit.TimeSpanDied = TimeSpan.FromSeconds(500 + (victim - 5) * 4);
            unit.PlayerKilledBy = killer;
            unit.UnitKilledBy = killerUnit;
        }

        FocusCard card = Builder()
            .Build(replay, FocusReplays.Catalog, new FocusReplayFacts("65550007", 65550007), Now);

        FocusCardNotable notable = Assert.Single(card.Notable);
        Assert.Equal("pentakill", notable.Kind);
        Assert.Equal("Hero 0", notable.Hero);
        Assert.Equal(500, notable.Second);
        Assert.True(notable.WipedTeam);
        Assert.Equal(2, FocusCard.CurrentVersion);
    }

    [Fact]
    public void Build_HasNoNotableEvent_WithoutAPentakill()
    {
        FocusCard card = Builder()
            .Build(
                FocusReplays.Replay(),
                FocusReplays.Catalog,
                new FocusReplayFacts("65550008", 65550008),
                Now
            );

        Assert.Empty(card.Notable);
    }

    [Fact]
    public void Build_RefusesAReplayWithoutAKey()
    {
        Assert.Throws<ArgumentException>(() =>
            Builder().Build(new Replay(), Array.Empty<Hero>(), new FocusReplayFacts(" "), Now)
        );
    }

    [Theory]
    [InlineData(1, "NA")]
    [InlineData(2, "EU")]
    [InlineData(3, "KR")]
    [InlineData(5, "CN")]
    [InlineData(98, null)]
    public void RegionName_NamesTheFourRegions(int id, string name) =>
        Assert.Equal(name, FocusCardBuilder.RegionName(id));

    private static FocusCardBuilder Builder() =>
        new(
            new GuideCatalog(
                new GuideReference
                {
                    CheckedAt = new DateOnly(2026, 10, 12),
                    Heroes = new[]
                    {
                        BuildFixtures.Guide(
                            BuildFixtures.Build(
                                "Teleport Build",
                                "Recommended",
                                "ynn",
                                "ynn",
                                "ynn",
                                "yn",
                                "ynn",
                                "ynn",
                                "ynnn"
                            )
                        ),
                    },
                }
            )
        );

    private static Hero RoledHero(string name, string unitId, string attributeId, string role) =>
        new(
            name,
            unitId,
            null,
            attributeId,
            role: role,
            talents: BuildFixtures.Hero(name, unitId).Talents
        );

    /// <summary>Ten players; the first is Li-Ming on the winning team 0 with the Teleport picks.</summary>
    private static Replay Replay(Hero star)
    {
        var players = new Player[10];
        for (int index = 0; index < 10; index++)
        {
            players[index] = new Player
            {
                Name = index == 0 ? "Star" : "Player" + index,
                BattleTag = index == 0 ? 1234 : 2000 + index,
                BattleNetRegionId = 2,
                PlayerType = PlayerType.Human,
                Team = index < 5 ? 0 : 1,
                IsWinner = index < 5,
                Character = index == 0 ? star.Name : "Somebody",
                ScoreResult = new ScoreResult
                {
                    SoloKills = index == 0 ? 9 : 1,
                    Deaths = index == 0 ? 1 : 5,
                    Assists = 4,
                    Takedowns = index == 0 ? 13 : 5,
                    MercCampCaptures = index == 0 ? 3 : 0,
                    MatchAwards =
                        index == 0
                            ? new List<MatchAwardType> { MatchAwardType.MVP }
                            : new List<MatchAwardType>(),
                },
                HeroUnits =
                    index == 0
                        ? new List<ReplayUnit>
                        {
                            new() { Name = star.UnitId, TimeSpanBorn = TimeSpan.Zero },
                        }
                        : new List<ReplayUnit>(),
                Talents =
                    index == 0
                        ? Enumerable
                            .Range(0, 7)
                            .Select(tier => new Talent { TalentName = $"T{tier}1" })
                            .ToArray()
                        : Array.Empty<Talent>(),
            };
        }

        return new Replay
        {
            Map = "Alterac Pass",
            GameMode = GameMode.StormLeague,
            ReplayVersion = "2.57.0.98348",
            Timestamp = new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc),
            Frames = 1200 * 16,
            Players = players,
        };
    }
}
