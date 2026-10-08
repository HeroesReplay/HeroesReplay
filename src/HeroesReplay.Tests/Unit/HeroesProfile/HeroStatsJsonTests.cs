using System.Collections.Generic;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>
/// Trimmed from live Heroes Profile answers for Valla, Storm League, patch 2.57 (2026-10-08).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroStatsJsonTests
{
    // enemy[].win_rate is Valla's loss rate against Xal'atath: "Lost against a team with Xal'atath 65.28% of games."
    private const string VallaMatchups = """
        {
          "ally": [
            {"hero":{"id":82,"name":"Whitemane","attribute_id":"WHIT"},"role":"Healer","wins":327,"losses":250,"games_played":577,"win_rate":56.67,"hovertext":"Won while on a team with Whitemane 56.67% of the time."},
            {"hero":{"id":1,"name":"Nobody","attribute_id":"NOPE"},"wins":0,"losses":0,"games_played":0,"win_rate":0}
          ],
          "enemy": [
            {"hero":{"id":92,"name":"Xal'atath","attribute_id":"HXAL"},"role":"Ranged Assassin","wins":117,"losses":220,"games_played":337,"win_rate":65.28,"hovertext":"Lost against a team with Xal'atath 65.28% of games."},
            {"hero":{"id":3,"name":"Alexstrasza","attribute_id":"Alex"},"wins":"127","losses":"93","win_rate":"42.27"}
          ],
          "combined": []
        }
        """;

    private const string MapStats = """
        {
          "Garden of Terror": {
            "average_win_rate": 50.0,
            "data": [
              {"name":"Valla","short_name":"valla","hero_id":64,"role":"Ranged Assassin","wins":299,"losses":254,"games_played":553,"win_rate":54.07,"ban_rate":9.76,"confidence_interval":4.15},
              {"name":"Xal'atath","hero_id":92,"wins":138,"losses":72,"games_played":210,"win_rate":65.71,"ban_rate":86.11}
            ]
          },
          "Cursed Hollow": {
            "data": [
              {"name":"Valla","hero_id":64,"wins":231,"losses":273,"games_played":504,"win_rate":45.83,"ban_rate":8.18}
            ]
          },
          "Silver City": { "data": [ {"name":"Valla","hero_id":64,"wins":0,"losses":0,"games_played":0} ] },
          "Lost Cavern": { "data": [] }
        }
        """;

    [Fact]
    public void ReadMatchups_ComputesTheWinRateFromWinsAndGamesNotTheInvertedField()
    {
        HeroMatchups matchups = HeroStatsJson.ReadMatchups(VallaMatchups);

        HeroPairStats xal = Assert.Single(matchups.Enemies, pair => pair.AttributeId == "HXAL");
        Assert.Equal(117, xal.Wins);
        Assert.Equal(337, xal.Games);
        Assert.Equal(34.7, xal.WinRate.Value, 1);

        HeroPairStats whitemane = Assert.Single(matchups.Allies);
        Assert.Equal("WHIT", whitemane.AttributeId);
        Assert.Equal(56.7, whitemane.WinRate.Value, 1);
    }

    [Fact]
    public void ReadMatchups_ReadsStringNumbersAndWinsPlusLossesWithoutGamesPlayed()
    {
        HeroMatchups matchups = HeroStatsJson.ReadMatchups(VallaMatchups);

        HeroPairStats alex = Assert.Single(matchups.Enemies, pair => pair.AttributeId == "Alex");
        Assert.Equal(127, alex.Wins);
        Assert.Equal(220, alex.Games);
        Assert.Equal(57.7, alex.WinRate.Value, 1);
    }

    [Fact]
    public void ReadMapStats_GroupsRowsByHeroAndSkipsMapsWithoutGames()
    {
        Dictionary<int, List<HeroMapStats>> maps = HeroStatsJson.ReadMapStats(MapStats);

        List<HeroMapStats> valla = maps[64];
        Assert.Equal(2, valla.Count);
        HeroMapStats garden = Assert.Single(valla, row => row.Map == "Garden of Terror");
        Assert.Equal(299, garden.Wins);
        Assert.Equal(553, garden.Games);
        Assert.Equal(9.76, garden.BanRate);
        Assert.Equal(86.11, Assert.Single(maps[92]).BanRate);
    }

    [Fact]
    public void ReadHeroes_KeepsHeroesWithAnIdNameAndAttributeId()
    {
        IReadOnlyList<HeroStatsHeroRef> heroes = HeroStatsJson.ReadHeroes(
            """
            {"heroes":[
              {"id":64,"name":"Valla","short_name":"valla","attribute_id":"Demo"},
              {"id":92,"name":"Xal'atath","attribute_id":"HXAL"},
              {"id":5,"name":"Missing"}
            ]}
            """
        );

        Assert.Equal(
            new[]
            {
                new HeroStatsHeroRef(64, "Valla", "Demo"),
                new HeroStatsHeroRef(92, "Xal'atath", "HXAL"),
            },
            heroes
        );
    }

    [Fact]
    public void ReadMajorPatches_ListsPatchesWithGlobalsNewestFirst()
    {
        IReadOnlyList<string> majors = HeroStatsJson.ReadMajorPatches(
            """
            {"patches":[
              {"game_version":"2.55.17.98025","valid_globals":true},
              {"game_version":"2.57.0.98304","valid_globals":true},
              {"game_version":"2.57.0.98348","valid_globals":true},
              {"game_version":"2.58.0.99000","valid_globals":false},
              {"game_version":"2.9.0.1","valid_globals":true}
            ],"oldest_patch":{}}
            """
        );

        Assert.Equal(new[] { "2.57", "2.55", "2.9" }, majors);
    }

    [Fact]
    public void Readers_ReturnNothingForBodiesThatAreNotJson()
    {
        Assert.Empty(HeroStatsJson.ReadHeroes("<html>"));
        Assert.Empty(HeroStatsJson.ReadMapStats(""));
        Assert.Empty(HeroStatsJson.ReadMatchups(null).Enemies);
        Assert.Empty(HeroStatsJson.ReadMajorPatches("{\"patches\":7}"));
        Assert.Null(HeroStatsJson.ReadSnapshot("not json"));
    }

    [Fact]
    public void Snapshot_RoundTripsWithoutTheComputedRates()
    {
        var snapshot = new HeroStatsSnapshot
        {
            Patch = "2.57",
            GameType = "sl",
            Heroes =
            {
                new HeroStats
                {
                    AttributeId = "Demo",
                    Name = "Valla",
                    Wins = 3629,
                    Games = 7226,
                    Maps =
                    {
                        new HeroMapStats
                        {
                            Map = "Garden of Terror",
                            Wins = 299,
                            Games = 553,
                            BanRate = 9.76,
                        },
                    },
                    Enemies =
                    {
                        new HeroPairStats
                        {
                            AttributeId = "HXAL",
                            Wins = 117,
                            Games = 337,
                        },
                    },
                },
            },
        };

        string text = HeroStatsJson.Write(snapshot);
        HeroStatsSnapshot read = HeroStatsJson.ReadSnapshot(text);

        Assert.DoesNotContain("WinRate", text, System.StringComparison.Ordinal);
        Assert.Equal("2.57", read.Patch);
        HeroStats valla = read.Find("demo");
        Assert.Equal(50.2, valla.WinRate.Value, 1);
        Assert.Equal(553, valla.Map("garden of terror").Games);
        Assert.Equal(117, valla.Enemy("HXAL").Wins);
        Assert.Null(valla.Ally("HXAL"));
    }

    [Fact]
    public void Math_WilsonLowerAndExpectedAgainst()
    {
        // Valla with Whitemane: 327 of 577.
        Assert.Equal(52.6, HeroStatsMath.WilsonLower(327, 577), 1);
        Assert.Equal(0, HeroStatsMath.WilsonLower(0, 0));
        // Xal'atath (65.9% overall) against Valla (50.2%) should win 65.7% from strength alone.
        Assert.Equal(65.7, HeroStatsMath.ExpectedAgainst(65.9, 50.2), 1);
        Assert.Null(HeroStatsMath.Rate(5, 0));
    }
}
