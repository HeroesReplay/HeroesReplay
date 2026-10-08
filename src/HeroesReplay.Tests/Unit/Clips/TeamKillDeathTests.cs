using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Shared;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Clips;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TeamKillDeathTests
{
    [Fact]
    public void FromReplay_OnePlayersHeroUnit_CountsFiveEnemyHeroUnits()
    {
        Replay replay = Match(out Player killer, out ReplayUnit killerUnit);
        Kill(replay, "Artanis", killer, killerUnit, 100);
        Kill(replay, "Butcher", killer, killerUnit, 103);
        Kill(replay, "Chromie", killer, killerUnit, 106);
        Kill(replay, "Diablo", killer, killerUnit, 109);
        Kill(replay, "E.T.C.", killer, killerUnit, 112);

        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(TeamKillDeaths.FromReplay(replay));

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal("Li-Ming", clip.Hero);
        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.True(clip.WipedTeam);
    }

    [Fact]
    public void FromReplay_TwoHeroUnitsOfOnePlayer_AreOneKiller()
    {
        var olaf = new ReplayUnit { Name = "HeroVikingOlaf" };
        var baelog = new ReplayUnit { Name = "HeroVikingBaleog" };
        Player killer = Hero("The Lost Vikings", 0, olaf, baelog);
        var players = new List<Player> { killer };
        players.Add(Dead("Artanis", killer, olaf, 100));
        players.Add(Dead("Butcher", killer, baelog, 103));
        players.Add(Dead("Chromie", killer, olaf, 106));
        players.Add(Dead("Diablo", killer, baelog, 109));
        players.Add(Dead("E.T.C.", killer, olaf, 112));

        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            TeamKillDeaths.FromReplay(ReplayOf(players))
        );

        TeamKillClip clip = Assert.Single(clips);
        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.Equal("The Lost Vikings", clip.Hero);
        Assert.True(clip.WipedTeam);
    }

    [Fact]
    public void FromReplay_TeammatesSharingTheWindow_AreNotAClip()
    {
        var arthasUnit = new ReplayUnit { Name = "HeroArthas" };
        var qhiraUnit = new ReplayUnit { Name = "HeroNexusHunter" };
        var limingUnit = new ReplayUnit { Name = "HeroWizard" };
        Player arthas = Hero("Arthas", 0, arthasUnit);
        Player qhira = Hero("Qhira", 0, qhiraUnit);
        Player liming = Hero("Li-Ming", 0, limingUnit);
        var players = new List<Player> { arthas, qhira, liming };
        players.Add(Dead("Imperius", arthas, arthasUnit, 532));
        players.Add(Dead("Raynor", arthas, arthasUnit, 533));
        players.Add(Dead("Lúcio", qhira, qhiraUnit, 533));
        players.Add(Dead("Azmodan", qhira, qhiraUnit, 538));
        players.Add(Dead("Johanna", liming, limingUnit, 539));

        Assert.Empty(TeamKillClips.Select(TeamKillDeaths.FromReplay(ReplayOf(players))));
    }

    [Fact]
    public void FromReplay_SummonBlow_DoesNotCount()
    {
        var heroUnit = new ReplayUnit { Name = "HeroAzmodan" };
        var summon = new ReplayUnit { Name = "AzmodanDemonWarrior" };
        Player killer = Hero("Azmodan", 0, heroUnit);
        var players = new List<Player> { killer };
        players.Add(Dead("Artanis", killer, heroUnit, 100));
        players.Add(Dead("Butcher", killer, heroUnit, 103));
        players.Add(Dead("Chromie", killer, heroUnit, 106));
        players.Add(Dead("Diablo", killer, heroUnit, 109));
        players.Add(Dead("E.T.C.", killer, summon, 112));

        Assert.Empty(TeamKillClips.Select(TeamKillDeaths.FromReplay(ReplayOf(players))));
    }

    [Fact]
    public void FromReplay_SuicideAndSameTeam_DoNotCount()
    {
        var unit = new ReplayUnit { Name = "HeroTyrael" };
        Player tyrael = Hero("Tyrael", 1, unit);
        tyrael.HeroUnits[0].TimeSpanDied = TimeSpan.FromSeconds(860);
        tyrael.HeroUnits[0].PlayerKilledBy = tyrael;
        Player ally = Hero("Tassadar", 1, new ReplayUnit { Name = "HeroTassadar" });
        var allyDeath = new ReplayUnit
        {
            TimeSpanDied = TimeSpan.FromSeconds(861),
            PlayerKilledBy = tyrael,
            UnitKilledBy = unit,
        };
        ally.HeroUnits.Add(allyDeath);

        Assert.Empty(TeamKillDeaths.FromReplay(ReplayOf(new List<Player> { tyrael, ally })));
    }

    [Fact]
    public void FromReplay_UsesTheEnglishCatalogName()
    {
        var killerUnit = new ReplayUnit { Name = "HeroTychus" };
        Player killer = Hero("Тайкус", 0, killerUnit);
        killer.HeroId = "Tychus";
        killer.HeroAttributeId = "Tych";
        var players = new List<Player> { killer };
        players.Add(CatalogVictim("Диабло", "Diablo", "Diab", killer, killerUnit, 100));
        players.Add(CatalogVictim("Рейнор", "Raynor", "Rayn", killer, killerUnit, 103));
        players.Add(CatalogVictim("Лусио", "Lucio", "Luci", killer, killerUnit, 106));
        players.Add(CatalogVictim("Мефисто", "Mephisto", "MEPH", killer, killerUnit, 109));
        players.Add(CatalogVictim("Леорик", "Leoric", "Leor", killer, killerUnit, 112));
        var heroes = new List<Hero>
        {
            new Hero("Tychus", "HeroTychus", "Tychus", "Tych"),
            new Hero("Diablo", "HeroDiablo", "Diablo", "Diab"),
            new Hero("Raynor", "HeroRaynor", "Raynor", "Rayn"),
            new Hero("Lúcio", "HeroLucio", "Lucio", "Luci"),
            new Hero("Mephisto", "HeroMephisto", "Mephisto", "MEPH"),
            new Hero("Leoric", "HeroLeoric", "Leoric", "Leor"),
        };

        IReadOnlyList<TeamKillDeath> deaths = TeamKillDeaths.FromReplay(ReplayOf(players), heroes);

        Assert.Equal(5, deaths.Count);
        Assert.Equal("Tychus", deaths[0].KillerHero);
        Assert.Contains(deaths, death => death.VictimHero == "Diablo");
        Assert.Contains(deaths, death => death.VictimHero == "Lúcio");
    }

    [Fact]
    public void FromReplay_Aram_NamesTheHeroesSpawnedNotTheLobbyHeroes()
    {
        // #348, replay 65745237: player 3 played Valla, and the lobby attribute id was Thrall's.
        var killerUnit = new ReplayUnit { Name = "HeroDemonHunter" };
        Player killer = Hero("Valla", 0, killerUnit);
        killer.HeroId = "Thrall";
        killer.HeroAttributeId = "Thra";
        var players = new List<Player> { killer };
        players.Add(AramVictim("Valla", "HeroDemonHunter", "ORPH", killer, killerUnit, 590));
        players.Add(AramVictim("Garrosh", "HeroGarrosh", "Murk", killer, killerUnit, 591));
        players.Add(AramVictim("Qhira", "HeroNexusHunter", "VALE", killer, killerUnit, 592));
        players.Add(AramVictim("Artanis", "HeroArtanis", "STUK", killer, killerUnit, 593));
        players.Add(AramVictim("Brightwing", "HeroFaerieDragon", "Vari", killer, killerUnit, 594));
        Replay replay = ReplayOf(players);
        replay.GameMode = GameMode.ARAM;
        var heroes = new List<Hero>
        {
            new Hero("Valla", "HeroDemonHunter", "Valla", "Demo"),
            new Hero("Thrall", "HeroThrall", "Thrall", "Thra"),
            new Hero("Orphea", "HeroOrphea", "Orphea", "ORPH"),
            new Hero("Garrosh", "HeroGarrosh", "Garrosh", "Garr"),
            new Hero("Murky", "HeroMurky", "Murky", "Murk"),
            new Hero("Qhira", "HeroNexusHunter", "Qhira", "NXHU"),
            new Hero("Valeera", "HeroValeera", "Valeera", "VALE"),
            new Hero("Artanis", "HeroArtanis", "Artanis", "Arts"),
            new Hero("Stukov", "HeroStukov", "Stukov", "STUK"),
            new Hero("Brightwing", "HeroFaerieDragon", "Brightwing", "Faer"),
            new Hero("Varian", "HeroVarian", "Varian", "Vari"),
        };

        IReadOnlyList<TeamKillDeath> deaths = TeamKillDeaths.FromReplay(replay, heroes);
        // #369: this replay was also cut as an identical team-wipe clip. One clip only.
        TeamKillClip clip = Assert.Single(TeamKillClips.Select(deaths));

        Assert.Equal(TeamKillClips.PentakillKind, clip.Kind);
        Assert.Equal("Valla", clip.Hero);
        Assert.All(deaths, death => Assert.Equal("Valla", death.KillerHero));
        Assert.Equal(
            new[] { "Valla", "Garrosh", "Qhira", "Artanis", "Brightwing" },
            deaths.Select(death => death.VictimHero)
        );
    }

    private static Player AramVictim(
        string character,
        string unit,
        string lobbyAttributeId,
        Player killer,
        ReplayUnit killerUnit,
        int second
    )
    {
        Player player = Dead(character, killer, killerUnit, second);
        player.HeroUnits[0].Name = unit;
        player.HeroAttributeId = lobbyAttributeId;
        return player;
    }

    private static Replay Match(out Player killer, out ReplayUnit killerUnit)
    {
        killerUnit = new ReplayUnit { Name = "HeroWizard" };
        killer = Hero("Li-Ming", 0, killerUnit);
        return ReplayOf(new List<Player> { killer });
    }

    private static void Kill(
        Replay replay,
        string victimName,
        Player killer,
        ReplayUnit killerUnit,
        int second
    )
    {
        var players = new List<Player>(replay.Players)
        {
            Dead(victimName, killer, killerUnit, second),
        };
        replay.Players = players.ToArray();
    }

    private static Player CatalogVictim(
        string character,
        string heroId,
        string attributeId,
        Player killer,
        ReplayUnit killerUnit,
        int second
    )
    {
        Player player = Dead(character, killer, killerUnit, second);
        player.HeroId = heroId;
        player.HeroAttributeId = attributeId;
        return player;
    }

    private static Player Dead(string name, Player killer, ReplayUnit killerUnit, int second)
    {
        return Hero(
            name,
            1,
            new ReplayUnit
            {
                TimeSpanDied = TimeSpan.FromSeconds(second),
                PlayerKilledBy = killer,
                UnitKilledBy = killerUnit,
            }
        );
    }

    private static Player Hero(string name, int team, params ReplayUnit[] units)
    {
        var player = new Player
        {
            Name = name,
            Character = name,
            Team = team,
            HeroUnits = new List<ReplayUnit>(units),
        };
        foreach (ReplayUnit unit in units)
        {
            unit.PlayerControlledBy = player;
        }

        return player;
    }

    private static Replay ReplayOf(List<Player> players)
    {
        return new Replay { Players = players.ToArray() };
    }
}
