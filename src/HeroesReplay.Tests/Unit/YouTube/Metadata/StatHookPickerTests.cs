using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>
/// Numbers come from Heroes Profile, Storm League, patch 2.57, on 2026-10-08, unless a test says
/// otherwise.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class StatHookPickerTests
{
    private static readonly StatHookSettings On = new StatHookSettings { Enabled = true };

    [Fact]
    public void Counter_NamesAMatchupThatBeatsWhatOverallRatesPredict()
    {
        // Both heroes win half their games, so 62% over 400 games is the matchup, not strength.
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5000, 10000, enemies: new[] { Pair("Alex", 248, 400) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );

        StatHook hook = Pick(stats, Blue("Valla"), Red("Alexstrasza"));

        Assert.Equal(StatHookKind.Counter, hook.Kind);
        Assert.Equal("Valla counters Alexstrasza", hook.Title);
        Assert.Equal(
            "Stats: Valla won 62.0% of 400 games against Alexstrasza, 12.0 points over the 50.0% their overall win rates predict (Storm League, patch 2.57).",
            hook.StatsLine
        );
    }

    [Fact]
    public void Counter_IsNotClaimedWhenOverallStrengthExplainsIt()
    {
        // Xal'atath beats Valla 65.3% of the time, but wins 65.9% of all her games: 65.7% expected.
        HeroStatsSnapshot stats = Snapshot(
            Hero("HXAL", "Xal'atath", 1639, 2487, enemies: new[] { Pair("Demo", 220, 337) }),
            Hero("Demo", "Valla", 3629, 7226, enemies: new[] { Pair("HXAL", 117, 337) })
        );

        StatHook hook = Pick(
            stats,
            new StatHookSettings { Enabled = true, PatchExtremes = false },
            Blue("Valla"),
            Red("Xal'atath")
        );

        Assert.Null(hook);
    }

    [Theory]
    [InlineData(124, 200)] // 62% but under 250 games
    [InlineData(141, 250)] // 56.4% is under 57%
    public void Counter_NeedsEnoughGamesAndRate(int wins, int games)
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5000, 10000, enemies: new[] { Pair("Alex", wins, games) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );

        Assert.Null(Pick(stats, Blue("Valla"), Red("Alexstrasza")));
    }

    [Fact]
    public void Counter_NeedsTheLowEndOfItsIntervalAboveTheExpectedRate()
    {
        // 57.7% of 260 games is 7.7 points over 50%, but its 95% interval starts at 51.6%,
        // under the 53% that Valla (53%) against a 50% hero should win anyway.
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5300, 10000, enemies: new[] { Pair("Alex", 150, 260) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );

        Assert.Null(Pick(stats, Blue("Valla"), Red("Alexstrasza")));
    }

    [Fact]
    public void Counter_IgnoresHeroesOnTheSameTeam()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5000, 10000, enemies: new[] { Pair("Alex", 248, 400) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );

        Assert.Null(Pick(stats, Blue("Valla"), Blue("Alexstrasza")));
    }

    [Fact]
    public void Duo_NamesTwoAlliesWhoWinTogether()
    {
        // Valla with Whitemane: 327 of 577 (56.7%, low end 52.6%).
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 3629, 7226, allies: new[] { Pair("WHIT", 327, 577) }),
            Hero("WHIT", "Whitemane", 5000, 10000)
        );

        StatHook hook = Pick(stats, Red("Valla"), Red("Whitemane"));

        Assert.Equal(StatHookKind.Duo, hook.Kind);
        Assert.Equal("Valla + Whitemane duo", hook.Title);
        Assert.StartsWith(
            "Stats: Valla and Whitemane won 56.7% of 577 games together",
            hook.StatsLine
        );
    }

    [Theory]
    [InlineData(165, 290)] // under 300 games
    [InlineData(170, 310)] // 54.8% is under 56%
    public void Duo_NeedsEnoughGamesAndRate(int wins, int games)
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 3629, 7226, allies: new[] { Pair("WHIT", wins, games) }),
            Hero("WHIT", "Whitemane", 5000, 10000)
        );

        Assert.Null(Pick(stats, Red("Valla"), Red("Whitemane")));
    }

    [Fact]
    public void Counter_ComesBeforeADuo()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Demo",
                "Valla",
                5000,
                10000,
                enemies: new[] { Pair("Alex", 248, 400) },
                allies: new[] { Pair("WHIT", 327, 577) }
            ),
            Hero("Alex", "Alexstrasza", 5000, 10000),
            Hero("WHIT", "Whitemane", 5000, 10000)
        );

        StatHook hook = Pick(stats, Blue("Valla"), Blue("Whitemane"), Red("Alexstrasza"));

        Assert.Equal(StatHookKind.Counter, hook.Kind);
    }

    [Fact]
    public void BestMap_WhenThisMapIsTheHerosTopMap()
    {
        // Genji: 56.4% of 179 on Braxis Holdout against 47.1% overall.
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Genj",
                "Genji",
                674,
                1431,
                maps: new[]
                {
                    Map("Braxis Holdout", 101, 179),
                    Map("Cursed Hollow", 80, 170),
                    Map("Sky Temple", 75, 160),
                    Map("Dragon Shire", 20, 40),
                }
            )
        );

        StatHook hook = Pick(stats, "Braxis Holdout", Blue("Genji"));
        StatHook elsewhere = Pick(stats, "Sky Temple", Blue("Genji"));

        Assert.Equal(StatHookKind.BestMap, hook.Kind);
        Assert.Equal("Genji's best map", hook.Title);
        Assert.Equal(
            "Stats: Genji won 56.4% of 179 games on Braxis Holdout, the hero's best map, 9.3 points over 47.1% overall (Storm League, patch 2.57).",
            hook.StatsLine
        );
        Assert.Null(elsewhere);
    }

    [Fact]
    public void BestMap_NeedsThreePointsOverTheOverallRate()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Genj",
                "Genji",
                500,
                1000,
                maps: new[] { Map("Braxis Holdout", 104, 200), Map("Cursed Hollow", 96, 200) }
            )
        );

        Assert.Null(Pick(stats, "Braxis Holdout", Blue("Genji")));
    }

    [Fact]
    public void SlipsTheBan_WhenTheHeroIsUsuallyBannedOnThisMap()
    {
        // Qhira: banned in 65.4% of Alterac Pass games, 314 games played.
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "NXHU",
                "Qhira",
                500,
                1000,
                maps: new[] { Map("Alterac Pass", 157, 314, ban: 65.4) }
            )
        );

        StatHook hook = Pick(stats, "Alterac Pass", Red("Qhira"));

        Assert.Equal(StatHookKind.SlipsTheBan, hook.Kind);
        Assert.Equal("Qhira slips the ban", hook.Title);
        Assert.StartsWith(
            "Stats: Qhira is banned in 65.4% of games on Alterac Pass",
            hook.StatsLine
        );
        Assert.Null(
            Pick(
                stats,
                "Alterac Pass",
                new StatHookSettings { Enabled = true, BanMinRate = 70 },
                Red("Qhira")
            )
        );
    }

    [Fact]
    public void BestMap_ComesBeforeSlipsTheBan()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Genj",
                "Genji",
                674,
                1431,
                maps: new[] { Map("Braxis Holdout", 101, 179), Map("Cursed Hollow", 80, 170) }
            ),
            Hero(
                "NXHU",
                "Qhira",
                500,
                1000,
                maps: new[] { Map("Braxis Holdout", 143, 286, ban: 66.2) }
            )
        );

        Assert.Equal(
            StatHookKind.BestMap,
            Pick(stats, "Braxis Holdout", Blue("Genji"), Red("Qhira")).Kind
        );
    }

    [Fact]
    public void WorstMap_WhenThisMapIsTheHerosLowestMap()
    {
        // Valla: 45.8% of 504 on Cursed Hollow against 50.2% overall.
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Demo",
                "Valla",
                3629,
                7226,
                maps: new[]
                {
                    Map("Cursed Hollow", 231, 504),
                    Map("Garden of Terror", 299, 553),
                    Map("Sky Temple", 265, 552),
                }
            )
        );

        StatHook hook = Pick(stats, "Cursed Hollow", Blue("Valla"));

        Assert.Equal(StatHookKind.WorstMap, hook.Kind);
        Assert.Equal("Valla's worst map", hook.Title);
        Assert.Contains("4.4 points under 50.2% overall", hook.StatsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchExtremes_NameATopOrBottomHeroOfThePatch()
    {
        var heroes = new List<HeroStats>
        {
            Hero("Mdvh", "Medivh", 335, 828), // 40.5%
            Hero("MalG", "Mal'Ganis", 1174, 2075), // 56.6%
        };
        for (int i = 0; i < 8; i++)
        {
            heroes.Add(Hero("H" + i, "Hero" + i, 500 + i, 1000));
        }

        HeroStatsSnapshot stats = Snapshot(heroes.ToArray());

        StatHook underdog = Pick(stats, Blue("Medivh"), Red("Hero3"));
        StatHook powerhouse = Pick(stats, Blue("Mal'Ganis"), Red("Hero3"));
        StatHook neither = Pick(stats, Blue("Hero3"), Red("Hero4"));

        Assert.Equal("Underdog Medivh", underdog.Title);
        Assert.Equal(StatHookKind.Underdog, underdog.Kind);
        Assert.Contains(
            "one of the 3 lowest win rates of the patch",
            underdog.StatsLine,
            StringComparison.Ordinal
        );
        Assert.Equal("Patch powerhouse Mal'Ganis", powerhouse.Title);
        Assert.Null(neither);
    }

    [Fact]
    public void NamedHeroes_AreSkippedAndSoAreTheirPairs()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5000, 10000, enemies: new[] { Pair("HXAL", 248, 400) }),
            Hero(
                "HXAL",
                "Xal'atath",
                5000,
                10000,
                maps: new[] { Map("Alterac Pass", 138, 210, ban: 86.1) }
            )
        );

        StatHook hook = StatHookPicker.Pick(
            stats,
            null,
            new[] { Blue("Valla"), Red("Xal'atath") },
            "Alterac Pass",
            new[] { "Xal'atath" },
            On,
            "Storm League"
        );

        Assert.Null(hook);
    }

    [Fact]
    public void Heroes_AreMatchedByCatalogAttributeId()
    {
        // The roster says "Lucio"; the statistics only know the attribute id.
        HeroStatsSnapshot stats = Snapshot(
            Hero("Luci", "Lúcio", 5000, 10000, enemies: new[] { Pair("Alex", 248, 400) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );
        var catalog = new[]
        {
            new Hero("Lucio", "HeroLucio", "Lucio", "Luci"),
            new Hero("Alexstrasza", "HeroAlexstrasza", "Alexstrasza", "Alex"),
        };

        StatHook hook = StatHookPicker.Pick(
            stats,
            catalog,
            new[] { Blue("Lucio"), Red("Alexstrasza") },
            "Dragon Shire",
            Array.Empty<string>(),
            On,
            "Storm League"
        );

        Assert.Equal("Lucio counters Alexstrasza", hook.Title);
    }

    [Fact]
    public void NothingIsPickedWithoutStatisticsOrRoster()
    {
        Assert.Null(
            StatHookPicker.Pick(null, null, new[] { Blue("Valla") }, "Sky Temple", null, On)
        );
        Assert.Null(
            StatHookPicker.Pick(
                Snapshot(),
                null,
                Array.Empty<ReplayMediaPlayer>(),
                "Sky Temple",
                null,
                On
            )
        );
        Assert.Null(Pick(Snapshot(Hero("Demo", "Valla", 1, 2)), Blue("Unknown")));
    }

    [Fact]
    public void EachHookCanBeTurnedOff()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero("Demo", "Valla", 5000, 10000, enemies: new[] { Pair("Alex", 248, 400) }),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );

        Assert.Null(
            Pick(
                stats,
                new StatHookSettings { Enabled = true, Counters = false },
                Blue("Valla"),
                Red("Alexstrasza")
            )
        );
    }

    private static StatHook Pick(HeroStatsSnapshot stats, params ReplayMediaPlayer[] roster) =>
        Pick(stats, "Dragon Shire", On, roster);

    private static StatHook Pick(
        HeroStatsSnapshot stats,
        string map,
        params ReplayMediaPlayer[] roster
    ) => Pick(stats, map, On, roster);

    private static StatHook Pick(
        HeroStatsSnapshot stats,
        StatHookSettings settings,
        params ReplayMediaPlayer[] roster
    ) => Pick(stats, "Dragon Shire", settings, roster);

    private static StatHook Pick(
        HeroStatsSnapshot stats,
        string map,
        StatHookSettings settings,
        params ReplayMediaPlayer[] roster
    ) =>
        StatHookPicker.Pick(
            stats,
            null,
            roster,
            map,
            Array.Empty<string>(),
            settings,
            "Storm League"
        );

    internal static HeroStatsSnapshot Snapshot(params HeroStats[] heroes) =>
        new HeroStatsSnapshot
        {
            Patch = "2.57",
            GameType = "sl",
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Heroes = heroes.ToList(),
        };

    internal static HeroStats Hero(
        string attributeId,
        string name,
        int wins,
        int games,
        HeroPairStats[] enemies = null,
        HeroPairStats[] allies = null,
        HeroMapStats[] maps = null
    ) =>
        new HeroStats
        {
            AttributeId = attributeId,
            Name = name,
            Wins = wins,
            Games = games,
            Enemies = (enemies ?? Array.Empty<HeroPairStats>()).ToList(),
            Allies = (allies ?? Array.Empty<HeroPairStats>()).ToList(),
            Maps = (maps ?? Array.Empty<HeroMapStats>()).ToList(),
        };

    internal static HeroPairStats Pair(string attributeId, int wins, int games) =>
        new HeroPairStats
        {
            AttributeId = attributeId,
            Wins = wins,
            Games = games,
        };

    internal static HeroMapStats Map(string map, int wins, int games, double ban = 5) =>
        new HeroMapStats
        {
            Map = map,
            Wins = wins,
            Games = games,
            BanRate = ban,
        };

    internal static ReplayMediaPlayer Blue(string hero) =>
        new ReplayMediaPlayer { Team = 0, Hero = hero };

    internal static ReplayMediaPlayer Red(string hero) =>
        new ReplayMediaPlayer { Team = 1, Hero = hero };
}
