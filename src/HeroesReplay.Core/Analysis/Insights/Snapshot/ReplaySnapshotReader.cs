using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
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

        return new ReplaySnapshot
        {
            Aram = replay.GameMode == GameMode.ARAM,
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
            Camps = events
                .Where(stat => stat.Name == CampInit && stat.Int("CampID") != null)
                .Select(stat => new Camp(
                    stat.Int("CampID").Value,
                    new MapPoint(stat.Int("PositionX") ?? 0, stat.Int("PositionY") ?? 0)
                ))
                .ToArray(),
            CampCaptures = events
                .Where(stat => stat.Name == CampCaptured && stat.Int("TeamID") is 1 or 2)
                .Select(stat => new CampCapture(
                    stat.Int("CampID") ?? -1,
                    stat.Text("CampType"),
                    stat.Int("TeamID").Value - 1,
                    stat.Time
                ))
                .ToArray(),
            TeamLevels = TeamLevels(replay),
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
                    },
        };
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
