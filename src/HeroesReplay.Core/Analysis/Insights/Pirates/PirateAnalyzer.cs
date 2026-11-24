using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Pirates;

/// <summary>
/// Finds a Juice Pirates game (<see cref="PirateInsights"/>). A team qualifies with a carrier (Lt.
/// Morales who took Medivac Dropship, or Medivh), at least <see cref="PirateInsightSettings.MinRaids"/>
/// raids, and a big share of the game's structure damage
/// (<see cref="PirateInsightSettings.MinStructureDamageShare"/>).
/// <para>
/// A dive: from gates open, sampled every <see cref="PirateInsightSettings.SampleEvery"/>, at least
/// <see cref="PirateInsightSettings.MinDivers"/> of the team's heroes within
/// <see cref="PirateInsightSettings.Radius"/> of an enemy fort, keep (still standing) or core, with
/// at most <see cref="PirateInsightSettings.MaxDefenders"/> enemy heroes there, for
/// <see cref="PirateInsightSettings.MinDiveSamples"/> samples in a row. Diving samples closer than
/// <see cref="PirateInsightSettings.DiveGap"/> are one dive. A normal late push brings defenders,
/// and a push after a won fight finds the defenders dead, so a dive also needs at least
/// <see cref="PirateInsightSettings.MinDefendersAlive"/> enemy heroes alive somewhere else.
/// </para>
/// <para>
/// A raid: a dive with the carrier in it (for Lt. Morales, once her team has level 10 and the
/// Medivac) that destroys at least
/// <see cref="PirateInsightSettings.MinRaidStructures"/> towers, forts or keeps. Three heroes
/// pushing a fort while the enemy takes an objective is a dive too, and that alone named replay
/// 66787940 (a Medivh game with no backdoor) Juice Pirates.
/// </para>
/// </summary>
public sealed class PirateAnalyzer : IInsightAnalyzer<PirateInsights>
{
    private readonly PirateInsightSettings settings;

    public PirateAnalyzer(PirateInsightSettings settings)
    {
        this.settings = settings ?? new PirateInsightSettings();
    }

    public PirateInsights Analyze(ReplaySnapshot snapshot)
    {
        if (
            snapshot?.Players == null
            || snapshot.Aram
            || snapshot.Positions == null
            || snapshot.Cores == null
            || snapshot.Cores.Count != 2
        )
        {
            return new PirateInsights();
        }

        PirateInsights best = null;
        for (int team = 0; team < 2; team++)
        {
            List<SnapshotPlayer> crew = snapshot
                .Players.Where(player => player.Team == team)
                .ToList();
            SnapshotPlayer carrier = Carrier(crew);
            if (carrier == null)
            {
                continue;
            }

            // Medivac Dropship is Lt. Morales' heroic: before level 10 she cannot fly anyone in.
            // Medivh's portal is a basic ability.
            TimeSpan flying = Is(carrier, "Lt. Morales")
                ? HeroicAt(snapshot, team)
                : snapshot.GatesOpen;
            List<PirateDive> log = Dives(snapshot, team, carrier.Index, flying);
            int raids = log.Count(dive => dive.Raid);
            if (
                best != null
                && (best.Raids > raids || (best.Raids == raids && best.Dives >= log.Count))
            )
            {
                continue;
            }

            double share = StructureDamageShare(snapshot, team);
            best = new PirateInsights
            {
                Pirates = raids >= settings.MinRaids && share >= settings.MinStructureDamageShare,
                Team = team,
                Carrier = carrier.Hero.Name,
                Sanctification = crew.Any(player =>
                    Is(player, "Tyrael") && HasTalent(player, settings.SanctificationTalent)
                ),
                Heroes = crew.Select(player => player.Hero?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray(),
                Dives = log.Count,
                Raids = raids,
                DiveLog = log,
                StructureDamageShare = share,
                Sunk = snapshot.CoreLostBy == 1 - team,
            };
        }

        return best ?? new PirateInsights();
    }

    private SnapshotPlayer Carrier(IReadOnlyList<SnapshotPlayer> crew)
    {
        SnapshotPlayer morales = crew.FirstOrDefault(player =>
            Is(player, "Lt. Morales") && HasTalent(player, settings.MedivacTalent)
        );
        if (morales != null)
        {
            return morales;
        }

        return settings.Medivh ? crew.FirstOrDefault(player => Is(player, "Medivh")) : null;
    }

    private static double StructureDamageShare(ReplaySnapshot snapshot, int team)
    {
        double ours = snapshot
            .Players.Where(player => player.Team == team)
            .Sum(player => (double)(player.Score?.StructureDamage ?? 0));
        double all = snapshot.Players.Sum(player => (double)(player.Score?.StructureDamage ?? 0));
        return all <= 0 ? 0 : ours / all;
    }

    /// <summary>When the team reached level 10, or the end when it never did.</summary>
    private static TimeSpan HeroicAt(ReplaySnapshot snapshot, int team) =>
        snapshot.TeamLevels != null
        && snapshot.TeamLevels.TryGetValue(team, out IReadOnlyDictionary<int, TimeSpan> levels)
        && levels != null
            ? levels
                .Where(level => level.Key >= 10)
                .Select(level => level.Value)
                .DefaultIfEmpty(snapshot.End)
                .Min()
            : snapshot.GatesOpen;

    private List<PirateDive> Dives(ReplaySnapshot snapshot, int team, int carrier, TimeSpan flying)
    {
        int enemy = 1 - team;
        int[] attackers = snapshot
            .Players.Where(p => p.Team == team)
            .Select(p => p.Index)
            .ToArray();
        int[] defenders = snapshot
            .Players.Where(p => p.Team == enemy)
            .Select(p => p.Index)
            .ToArray();
        List<Town> towns = (snapshot.Towns ?? Array.Empty<Town>())
            .Where(town => town.Team == enemy)
            .ToList();
        MapPoint core = snapshot.Cores[enemy];

        var windows = new List<Window>();
        Window open = null;
        int streak = 0;
        TimeSpan step =
            settings.SampleEvery <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : settings.SampleEvery;
        for (TimeSpan time = snapshot.GatesOpen; time <= snapshot.End; time += step)
        {
            // A push after a won fight also finds an empty base, but the defenders are dead. A
            // backdoor happens while they are alive and somewhere else.
            int alive = defenders.Count(index => snapshot.Positions.TryGet(index, time, out _));
            MapPoint? target =
                alive >= settings.MinDefendersAlive
                    ? Targets(snapshot, towns, core, time)
                        .Cast<MapPoint?>()
                        .FirstOrDefault(point =>
                            Near(snapshot, attackers, point.Value, time, settings.Radius)
                                >= settings.MinDivers
                            && Near(snapshot, defenders, point.Value, time, settings.Radius)
                                <= settings.MaxDefenders
                        )
                    : null;
            if (target == null)
            {
                streak = 0;
                continue;
            }

            streak++;
            int divers = Near(snapshot, attackers, target.Value, time, settings.Radius);
            bool carrierHere =
                snapshot.Positions.TryGet(carrier, time, out MapPoint carrierAt)
                && carrierAt.DistanceTo(target.Value) <= settings.StructureRadius;
            if (open != null && time - open.End < settings.DiveGap)
            {
                // Still the same dive (or a new streak close enough to be part of it).
                open.End = time;
                open.Divers = Math.Max(open.Divers, divers);
                open.Carrier |= carrierHere;
                continue;
            }

            if (streak < settings.MinDiveSamples)
            {
                continue;
            }

            TimeSpan start = time - step * (settings.MinDiveSamples - 1);
            open = new Window
            {
                Start = start,
                End = time,
                Divers = divers,
                Carrier = carrierHere,
            };
            windows.Add(open);
        }

        return windows
            .Select(window =>
            {
                int structures = Structures(snapshot, attackers, enemy, window);
                bool raid =
                    window.End >= flying
                    && (window.Carrier || !settings.CarrierMustDive)
                    && structures >= settings.MinRaidStructures;
                return new PirateDive(
                    window.Start,
                    window.End,
                    window.Divers,
                    window.Carrier,
                    structures,
                    raid
                );
            })
            .ToList();
    }

    /// <summary>
    /// The enemy towers, forts and keeps that fell during the dive with a pirate hero close by and
    /// no last hit from the defenders' side.
    /// </summary>
    private int Structures(ReplaySnapshot snapshot, int[] attackers, int enemy, Window window)
    {
        TimeSpan from = window.Start - settings.SampleEvery;
        TimeSpan to = window.End + settings.RaidGrace;
        return (snapshot.StructureLosses ?? Array.Empty<StructureLoss>()).Count(loss =>
            loss.Team == enemy
            && loss.Time >= from
            && loss.Time <= to
            && (loss.KillerIndex < 0 || attackers.Contains(loss.KillerIndex))
            && (
                attackers.Contains(loss.KillerIndex)
                || Near(snapshot, attackers, loss.Point, loss.Time, settings.StructureRadius) > 0
            )
        );
    }

    private static IEnumerable<MapPoint> Targets(
        ReplaySnapshot snapshot,
        IReadOnlyList<Town> towns,
        MapPoint core,
        TimeSpan time
    )
    {
        foreach (Town town in towns)
        {
            bool fallen = (snapshot.TownLosses ?? Array.Empty<TownLoss>()).Any(loss =>
                loss.TownId == town.Id && loss.Time <= time
            );
            if (!fallen)
            {
                yield return town.Point;
            }
        }

        yield return core;
    }

    private static int Near(
        ReplaySnapshot snapshot,
        IEnumerable<int> players,
        MapPoint target,
        TimeSpan time,
        double radius
    ) =>
        players.Count(index =>
            snapshot.Positions.TryGet(index, time, out MapPoint point)
            && point.DistanceTo(target) <= radius
        );

    private static bool Is(SnapshotPlayer player, string hero) =>
        string.Equals(player?.Hero?.Name, hero, StringComparison.OrdinalIgnoreCase);

    private static bool HasTalent(SnapshotPlayer player, string talent) =>
        !string.IsNullOrWhiteSpace(talent)
        && (player.Talents ?? Array.Empty<string>()).Any(name =>
            name != null && name.Contains(talent, StringComparison.OrdinalIgnoreCase)
        );

    private sealed class Window
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public int Divers { get; set; }
        public bool Carrier { get; set; }
    }
}
