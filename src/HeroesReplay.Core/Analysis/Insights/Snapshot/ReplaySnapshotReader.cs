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

    /// <summary>Forts (<c>TownTownHallL2</c>) and keeps (<c>TownTownHallL3</c>).</summary>
    private const string TownHallPrefix = "TownTownHall";

    /// <summary>
    /// The boss dies at most this long before its camp's capture event (#528). The capture
    /// follows the death by 2 s at the median, but a contested circle holds it back: 25 of 1,275
    /// captures in the production corpus took more than 10 s, the longest 29 s, and those are the
    /// captures where the other team may take the boss.
    /// </summary>
    private static readonly TimeSpan BossKillWindow = TimeSpan.FromSeconds(30);

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
        HeroPositionFill.Apply(replay);
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
            GatesOpen = events.FirstOrDefault(stat => stat.Name == GatesOpen)?.Time
                ?? TimeSpan.Zero,
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
            StructureLosses = StructureLosses(replay, players),
            Camps = camps,
            CampCaptures = captures,
            TeamLevels = TeamLevels(replay),
            KillingBlows = KillingBlows(replay, catalog),
            BossCaptures = BossCaptures(replay, players, camps, captures),
            HeroDeaths = HeroDeaths(replay, players),
            HearthCasts = HearthCasts(replay, players, settings.BSteps),
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
                        StructureDamage = score.StructureDamage,
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
                        MapValues = player.MiscellaneousScoreResultEventDictionary
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

    /// <summary>
    /// The game's own hero deaths (<see cref="HeroKills"/>), so D.Va's mech, a spirit of Chen or a
    /// copy by Abathur is not a death and Leoric's is. A replay without those events falls back
    /// to the deaths of hero units.
    /// </summary>
    private static HeroDeath[] HeroDeaths(Replay replay, Player[] players)
    {
        IReadOnlyList<HeroKill> kills = HeroKills.Read(replay);
        if (kills != null)
        {
            return kills
                .Where(kill => kill.Point != null)
                .Select(kill => new HeroDeath(
                    kill.VictimIndex,
                    players[kill.VictimIndex].Team,
                    kill.Time,
                    kill.Point.Value,
                    kill.Body?.PlayerKilledBy != null
                        ? Array.IndexOf(players, kill.Body.PlayerKilledBy)
                        : kill.BlowIndex
                ))
                .OrderBy(death => death.Time)
                .ThenBy(death => death.PlayerIndex)
                .ToArray();
        }

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
                            new MapPoint(unit.PointDied.X, unit.PointDied.Y),
                            unit.PlayerKilledBy == null
                                ? -1
                                : Array.IndexOf(players, unit.PlayerKilledBy)
                        )
                    );
                }
            }
        }

        return deaths.OrderBy(death => death.Time).ThenBy(death => death.PlayerIndex).ToArray();
    }

    /// <summary>
    /// Each Hearthstone order (<see cref="BStepInsightSettings.HearthLinks"/> for the replay's
    /// build) with the player's next order: a move cancels it when that next order is a plain
    /// right-click (no ability: a point, a unit, or a held right mouse button updating the point).
    /// </summary>
    private static HearthCast[] HearthCasts(
        Replay replay,
        Player[] players,
        BStepInsightSettings settings
    )
    {
        int? link = settings?.HearthLink(replay.ReplayBuild);
        if (link == null || replay.GameEvents == null)
        {
            return Array.Empty<HearthCast>();
        }

        var indexOf = new Dictionary<Player, int>();
        for (int index = 0; index < players.Length; index++)
        {
            if (players[index] != null)
            {
                indexOf.TryAdd(players[index], index);
            }
        }

        var casts = new List<HearthCast>();
        var open = new Dictionary<int, HearthCast>();
        foreach (
            GameEvent order in replay
                .GameEvents.Where(order =>
                    order?.player != null
                    && order.eventType
                        is GameEventType.CCmdEvent
                            or GameEventType.CCmdUpdateTargetPointEvent
                )
                .OrderBy(order => order.ticksElapsed)
        )
        {
            if (!indexOf.TryGetValue(order.player, out int index))
            {
                continue;
            }

            double seconds = order.ticksElapsed / 16.0;
            OrderKind kind = Kind(order, link.Value);
            if (open.Remove(index, out HearthCast cast))
            {
                casts.Add(kind == OrderKind.Move ? cast with { CancelSeconds = seconds } : cast);
            }

            if (kind == OrderKind.Hearth)
            {
                open[index] = new HearthCast(index, seconds);
            }
        }

        casts.AddRange(open.Values);
        return casts.OrderBy(cast => cast.Seconds).ThenBy(cast => cast.PlayerIndex).ToArray();
    }

    private enum OrderKind
    {
        Move,
        Hearth,
        Other,
    }

    private static OrderKind Kind(GameEvent order, int hearthLink)
    {
        if (order.eventType == GameEventType.CCmdUpdateTargetPointEvent)
        {
            return OrderKind.Move;
        }

        TrackerEventStructure ability = order.data?.array?.Length > 1 ? order.data.array[1] : null;
        if (ability == null)
        {
            return OrderKind.Move;
        }

        return ability.array?[0]?.unsignedInt == (ulong)hearthLink
            ? OrderKind.Hearth
            : OrderKind.Other;
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

    private static StructureLoss[] StructureLosses(Replay replay, Player[] players)
    {
        var indexOf = new Dictionary<Player, int>();
        for (int index = 0; index < players.Length; index++)
        {
            if (players[index] != null)
            {
                indexOf.TryAdd(players[index], index);
            }
        }

        return (replay.Units ?? new List<Unit>())
            .Where(unit =>
                unit?.Team is 0 or 1
                && unit.TimeSpanDied != null
                && unit.PointBorn != null
                && (
                    unit.Name?.StartsWith(TowerPrefix, StringComparison.Ordinal) == true
                    || unit.Name?.StartsWith(TownHallPrefix, StringComparison.Ordinal) == true
                )
            )
            .Select(unit => new StructureLoss(
                unit.Team.Value,
                unit.TimeSpanDied.Value,
                new MapPoint(unit.PointBorn.X, unit.PointBorn.Y),
                unit.PlayerKilledBy != null && indexOf.TryGetValue(unit.PlayerKilledBy, out int killer)
                    ? killer
                    : -1
            ))
            .OrderBy(loss => loss.Time)
            .ToArray();
    }

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
