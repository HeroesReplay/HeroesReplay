using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Analysis.Insights.Snapshot;

/// <summary>
/// Reads a parsed replay into a <see cref="ReplaySnapshot"/> (#412). Heroes come from the catalog
/// by their spawned unit, so names are English whatever the replay's game language.
/// </summary>
public static class ReplaySnapshotReader
{
    private const string GatesOpen = "GatesOpen";
    private const string TownInit = "TownStructureInit";
    private const string TownDeath = "TownStructureDeath";
    private const string CampInit = "JungleCampInit";
    private const string CampCaptured = "JungleCampCapture";
    private const string TowerPrefix = "TownCannonTower";

    /// <summary>The boss dies at most this long before its camp's capture event (#528).</summary>
    private static readonly TimeSpan BossKillWindow = TimeSpan.FromSeconds(10);

    /// <summary>The boss dies within this many map units of its camp's position (#528).</summary>
    private const double BossCampRadius = 20;

    public static ReplaySnapshot Read(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        InsightsSettings settings
    )
    {
        if (replay == null)
        {
            throw new ArgumentNullException(nameof(replay));
        }

        settings ??= new InsightsSettings();
        Player[] players = replay.Players ?? Array.Empty<Player>();
        List<StatEvent> events = (replay.TrackerEvents ?? new List<TrackerEvent>())
            .Select(StatEvent.From)
            .Where(stat => stat != null)
            .ToList();
        Dictionary<int, Unit> cores = Cores(replay, settings);
        Unit fallen = cores
            .Values.Where(core => core.TimeSpanDied != null)
            .OrderBy(core => core.TimeSpanDied)
            .FirstOrDefault();
        List<Town> towns = Towns(events);
        Camp[] camps = events
            .Where(stat => stat.Name == CampInit && stat.Int("CampID") != null)
            .Select(stat => new Camp(
                stat.Int("CampID").Value,
                new MapPoint(stat.Int("PositionX") ?? 0, stat.Int("PositionY") ?? 0)
            ))
            .ToArray();
        CampCapture[] captures = events
            .Where(stat => stat.Name == CampCaptured && stat.Int("TeamID") is 1 or 2)
            .Select(stat => new CampCapture(
                stat.Int("CampID") ?? -1,
                stat.Text("CampType"),
                stat.Int("TeamID").Value - 1,
                stat.Time
            ))
            .ToArray();

        return new ReplaySnapshot
        {
            Aram = replay.GameMode == GameMode.ARAM,
            Map = EnglishMapNames.Prefer(null, replay.Map, replay.MapAlternativeName),
            GatesOpen =
                events.FirstOrDefault(stat => stat.Name == GatesOpen)?.Time ?? TimeSpan.Zero,
            End = fallen?.TimeSpanDied ?? replay.ReplayLength,
            CoreLostBy = fallen?.Team,
            Players = players
                .Select((player, index) => Player(replay, player, index, catalog))
                .Where(player => player != null)
                .ToArray(),
            Cores = cores.ToDictionary(
                pair => pair.Key,
                pair => new MapPoint(pair.Value.PointBorn.X, pair.Value.PointBorn.Y)
            ),
            Towns = towns,
            TownLosses = TownLosses(events, towns),
            TowerLosses = (replay.Units ?? new List<Unit>())
                .Where(unit =>
                    unit?.Team is 0 or 1
                    && unit.TimeSpanDied != null
                    && unit.Name?.StartsWith(TowerPrefix, StringComparison.Ordinal) == true
                )
                .Select(unit => new TowerLoss(unit.Team.Value, unit.TimeSpanDied.Value))
                .ToArray(),
            Camps = camps,
            CampCaptures = captures,
            TeamLevels = TeamLevels(replay),
            KillingBlows = KillingBlows(replay, catalog),
            BossCaptures = BossCaptures(replay, players, camps, captures),
            HeroDeaths = HeroDeaths(players),
            Positions = new HeroPositions(players, settings.MaxPositionAge),
        };
    }

    private static SnapshotPlayer Player(
        Replay replay,
        Player player,
        int index,
        IReadOnlyList<Hero> catalog
    )
    {
        if (player == null || player.PlayerType == PlayerType.Spectator)
        {
            return null;
        }

        ScoreResult score = player.ScoreResult;
        return new SnapshotPlayer
        {
            Index = index,
            Team = player.Team,
            Hero = PlayedHero.Find(catalog, replay, player),
            IsAi = player.PlayerType == PlayerType.Computer,
            Talents = (player.Talents ?? Array.Empty<Talent>())
                .Select(talent => talent?.TalentName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray(),
            Score =
                score == null
                    ? new PlayerScore()
                    : new PlayerScore
                    {
                        Kills = score.SoloKills,
                        Deaths = score.Deaths,
                        Assists = score.Assists,
                        Takedowns = score.Takedowns,
                        HeroDamage = score.HeroDamage,
                        SiegeDamage = score.SiegeDamage,
                        MinionDamage = score.MinionDamage,
                        Healing = score.Healing ?? 0,
                        Experience = score.ExperienceContribution,
                        HighestKillStreak = score.HighestKillStreak,
                        Mvp = score.MatchAwards?.Contains(MatchAwardType.MVP) == true,
                        Multikill = score.Multikill,
                        ClutchHeals = score.ClutchHealsPerformed,
                        Vengeances = score.VengeancesPerformed,
                        Escapes = score.EscapesPerformed,
                        TeamfightEscapes = score.TeamfightEscapesPerformed,
                        OutnumberedDeaths = score.OutnumberedDeaths,
                        Protection = score.ProtectionGivenToAllies,
                        CrowdControl = score.TimeCCdEnemyHeroes ?? TimeSpan.Zero,
                        OnFire = score.OnFireTimeonFire ?? TimeSpan.Zero,
                        Awards = (score.MatchAwards ?? new List<MatchAwardType>())
                            .Select(award => award.ToString())
                            .ToArray(),
                        MapValues =
                            player.MiscellaneousScoreResultEventDictionary
                            ?? new Dictionary<string, int>(),
                    },
        };
    }

    /// <summary>Each player's hero killing blows, by the clips' <c>TeamKillDeaths</c> rule.</summary>
    private static KillingBlow[] KillingBlows(Replay replay, IReadOnlyList<Hero> catalog) =>
        TeamKillDeaths
            .FromReplay(replay, catalog)
            .Where(blow => blow.KillerKey >= 0)
            .OrderBy(blow => blow.Second)
            .ThenBy(blow => blow.KillerKey)
            .Select(blow => new KillingBlow(blow.Second, blow.KillerKey))
            .ToArray();

    /// <summary>
    /// Each boss camp capture (#528) at its camp's position, with the player whose unit killed the
    /// boss: the neutral unit that died at the camp in the <see cref="BossKillWindow"/> before the
    /// capture, the latest first. A killed boss is captured by standing in its circle, so the team
    /// that killed it is not always the team that captured it. A capture with neither a camp
    /// position nor that unit is left out, since nothing places it on the map.
    /// </summary>
    private static BossCapture[] BossCaptures(
        Replay replay,
        Player[] players,
        Camp[] camps,
        CampCapture[] captures
    )
    {
        var bosses = new List<BossCapture>();
        foreach (
            CampCapture capture in captures.Where(capture =>
                capture.CampType?.Contains("Boss", StringComparison.OrdinalIgnoreCase) == true
            )
        )
        {
            MapPoint? camp = camps
                .Where(entry => entry.Id == capture.CampId)
                .Select(entry => (MapPoint?)entry.Point)
                .FirstOrDefault();
            Unit boss = (replay.Units ?? new List<Unit>())
                .Where(unit =>
                    unit != null
                    && unit.Team is not (0 or 1)
                    && unit.TimeSpanDied is TimeSpan died
                    && died <= capture.Time
                    && died >= capture.Time - BossKillWindow
                    && unit.PlayerKilledBy != null
                    && unit.PointDied != null
                    && (
                        camp == null
                        || new MapPoint(unit.PointDied.X, unit.PointDied.Y).DistanceTo(camp.Value)
                            <= BossCampRadius
                    )
                )
                .OrderBy(unit => capture.Time - unit.TimeSpanDied.Value)
                .FirstOrDefault();
            if (camp == null && boss == null)
            {
                continue;
            }

            bosses.Add(
                new BossCapture(
                    capture.CampId,
                    capture.Team,
                    capture.Time,
                    camp ?? new MapPoint(boss.PointDied.X, boss.PointDied.Y),
                    boss == null ? -1 : Array.IndexOf(players, boss.PlayerKilledBy)
                )
            );
        }

        return bosses.OrderBy(boss => boss.Time).ToArray();
    }

    private static HeroDeath[] HeroDeaths(Player[] players)
    {
        var deaths = new List<HeroDeath>();
        for (int index = 0; index < players.Length; index++)
        {
            Player player = players[index];
            foreach (Unit unit in player?.HeroUnits ?? new List<Unit>())
            {
                if (unit?.TimeSpanDied is TimeSpan died && unit.PointDied != null)
                {
                    deaths.Add(
                        new HeroDeath(
                            index,
                            player.Team,
                            died,
                            new MapPoint(unit.PointDied.X, unit.PointDied.Y)
                        )
                    );
                }
            }
        }

        return deaths.OrderBy(death => death.Time).ThenBy(death => death.PlayerIndex).ToArray();
    }

    private static Dictionary<int, Unit> Cores(Replay replay, InsightsSettings settings)
    {
        var names = new HashSet<string>(
            settings.CoreUnits ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase
        );
        var cores = new Dictionary<int, Unit>();
        foreach (
            Unit unit in (replay.Units ?? new List<Unit>())
                .Where(unit => unit?.Team is 0 or 1 && names.Contains(unit.Name))
                .OrderBy(unit => unit.TimeSpanBorn)
        )
        {
            cores.TryAdd(unit.Team.Value, unit);
        }

        return cores;
    }

    private static List<Town> Towns(List<StatEvent> events) =>
        events
            .Where(stat =>
                stat.Name == TownInit && stat.Int("TownID") != null && stat.Int("Team") is 1 or 2
            )
            .Select(stat => new Town(
                stat.Int("TownID").Value,
                stat.Int("Team").Value - 1,
                stat.Int("Lane") ?? 0,
                new MapPoint(stat.Int("PositionX") ?? 0, stat.Int("PositionY") ?? 0)
            ))
            .ToList();

    private static TownLoss[] TownLosses(List<StatEvent> events, List<Town> towns)
    {
        Dictionary<int, int> teams = towns
            .GroupBy(town => town.Id)
            .ToDictionary(group => group.Key, group => group.First().Team);
        return events
            .Where(stat =>
                stat.Name == TownDeath && stat.Int("TownID") is int id && teams.ContainsKey(id)
            )
            .Select(stat => new TownLoss(
                stat.Int("TownID").Value,
                teams[stat.Int("TownID").Value],
                (stat.Text("UnitType") ?? string.Empty).EndsWith("L3", StringComparison.Ordinal),
                stat.Time
            ))
            .ToArray();
    }

    private static Dictionary<int, IReadOnlyDictionary<int, TimeSpan>> TeamLevels(Replay replay)
    {
        var levels = new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>();
        for (int team = 0; team < (replay.TeamLevels?.Length ?? 0) && team < 2; team++)
        {
            if (replay.TeamLevels[team] != null)
            {
                levels[team] = new Dictionary<int, TimeSpan>(replay.TeamLevels[team]);
            }
        }

        return levels;
    }
}
