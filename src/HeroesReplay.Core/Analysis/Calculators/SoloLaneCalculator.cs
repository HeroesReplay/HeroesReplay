using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// A solo laner's duel (#437). Each team's solo laner comes from <see cref="LaneAnalyzer"/>. A
/// solo laner with one enemy hero within <c>Spectate:MaxDistanceToEnemy</c>, and no other hero
/// within that of either, for at least <c>Spectate:SoloLaneDuelTime</c>, is in a duel. It scores
/// <see cref="WeightSettings.SoloLaneDuel"/>, as much as a 4v4, so a fight elsewhere that size or
/// smaller no longer takes the camera from it. What decides something still does: a kill, a
/// death and every second of its lead-in (<see cref="ReplayTimeline.ApplyDeathContext"/>), a map
/// objective, a 4v5 or a 5v5. The whole duel follows one duelist: the one whose hero dies within
/// <c>Spectate:MaxPastDeathContextTime</c> of its end, so the duel runs into that death's
/// lead-in, else the solo laner, else the lower player slot.
/// </summary>
public class SoloLaneCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly Func<Replay, IReadOnlyCollection<Player>> soloLaners;

    public SoloLaneCalculator(
        AppSettings settings,
        IGameData gameData,
        ILogger<SoloLaneCalculator> logger
    )
        : this(
            settings,
            replay =>
                SoloLaners(
                    replay,
                    gameData?.Heroes,
                    settings?.Insights,
                    logger ?? NullLogger<SoloLaneCalculator>.Instance
                )
        ) { }

    internal SoloLaneCalculator(
        AppSettings settings,
        Func<Replay, IReadOnlyCollection<Player>> soloLaners
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.soloLaners = soloLaners ?? throw new ArgumentNullException(nameof(soloLaners));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        float weight = settings.Weights?.SoloLaneDuel ?? 0f;
        int maxDistance = settings.Spectate?.MaxDistanceToEnemy ?? 0;
        if (weight <= 0f || maxDistance <= 0)
        {
            return;
        }

        IReadOnlyCollection<Player> solo = soloLaners(timeline.Replay);
        if (solo == null || solo.Count == 0)
        {
            return;
        }

        Player[] players = timeline.Replay.Players ?? Array.Empty<Player>();
        int shortest = Math.Max(1, (int)settings.Spectate.SoloLaneDuelTime.TotalSeconds);
        int horizon = (int)settings.Spectate.MaxPastDeathContextTime.TotalSeconds;
        var heroes = new List<(Unit Unit, Point Point)>(10);
        var open = new Dictionary<(Unit Laner, Unit Enemy), int>();

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            heroes.Clear();
            foreach (Unit unit in timeline.AliveHeroesAt(second))
            {
                if (
                    (unit.Team == 0 || unit.Team == 1)
                    && unit.PlayerControlledBy != null
                    && timeline.TryGetPoint(unit, second, out Point point)
                )
                {
                    heroes.Add((unit, point));
                }
            }

            List<(Unit Laner, Unit Enemy)> now = Duels(heroes, solo, maxDistance);
            foreach ((Unit Laner, Unit Enemy) duel in now)
            {
                open.TryAdd(duel, second);
            }

            foreach (KeyValuePair<(Unit Laner, Unit Enemy), int> duel in open.ToList())
            {
                if (!now.Contains(duel.Key))
                {
                    open.Remove(duel.Key);
                    OfferDuel(duel.Key, duel.Value, second - 1);
                }
            }
        }

        foreach (KeyValuePair<(Unit Laner, Unit Enemy), int> duel in open)
        {
            OfferDuel(duel.Key, duel.Value, timeline.TotalSeconds - 1);
        }

        void OfferDuel((Unit Laner, Unit Enemy) duel, int start, int last)
        {
            if (last - start + 1 < shortest)
            {
                return;
            }

            Unit hero = Follow(duel.Laner, duel.Enemy, last, horizon, solo, players);
            Unit other = hero == duel.Laner ? duel.Enemy : duel.Laner;
            timeline.OfferRange(
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(last),
                GetType(),
                hero,
                hero.PlayerControlledBy,
                weight,
                $"{hero.PlayerControlledBy.Character} duels {other.PlayerControlledBy.Character} in a solo lane ({last - start + 1}s)"
            );
        }
    }

    /// <summary>
    /// Each team's solo laner from the laning window (<see cref="LaneAnalyzer"/>), or none on
    /// ARAM, on a replay without lanes, or when the replay cannot be read for them.
    /// </summary>
    public static IReadOnlyCollection<Player> SoloLaners(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        InsightsSettings insights,
        ILogger logger
    )
    {
        Player[] players = replay?.Players ?? Array.Empty<Player>();
        if (players.Length == 0)
        {
            return Array.Empty<Player>();
        }

        insights ??= new InsightsSettings();
        try
        {
            LaneInsights lanes = new LaneAnalyzer(insights.Lanes).Analyze(
                ReplaySnapshotReader.Read(replay, catalog, insights)
            );
            return lanes
                .Teams.Select(team => team.SoloLaner?.PlayerIndex ?? -1)
                .Where(index => index >= 0 && index < players.Length)
                .Select(index => players[index])
                .ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            logger?.LogWarning(
                e,
                "Could not find the solo laners; the focus timeline has no solo-lane duels."
            );
            return Array.Empty<Player>();
        }
    }

    /// <summary>
    /// A solo laner and the one enemy hero within <paramref name="maxDistance"/>, with no other
    /// hero within that of either. Two solo laners in a duel are one duel.
    /// </summary>
    private static List<(Unit Laner, Unit Enemy)> Duels(
        List<(Unit Unit, Point Point)> heroes,
        IReadOnlyCollection<Player> solo,
        int maxDistance
    )
    {
        var duels = new List<(Unit Laner, Unit Enemy)>();
        for (int i = 0; i < heroes.Count; i++)
        {
            if (!solo.Contains(heroes[i].Unit.PlayerControlledBy))
            {
                continue;
            }

            List<int> near = Near(heroes, i, maxDistance);
            if (near.Count != 1 || heroes[near[0]].Unit.Team == heroes[i].Unit.Team)
            {
                continue;
            }

            int j = near[0];
            if (Near(heroes, j, maxDistance).Count != 1)
            {
                continue;
            }

            if (solo.Contains(heroes[j].Unit.PlayerControlledBy) && j < i)
            {
                continue;
            }

            duels.Add((heroes[i].Unit, heroes[j].Unit));
        }

        return duels;
    }

    private static List<int> Near(List<(Unit Unit, Point Point)> heroes, int i, int maxDistance)
    {
        var near = new List<int>(2);
        for (int j = 0; j < heroes.Count; j++)
        {
            if (j != i && heroes[i].Point.DistanceTo(heroes[j].Point) <= maxDistance)
            {
                near.Add(j);
            }
        }

        return near;
    }

    /// <summary>
    /// The duelist whose hero dies first within <paramref name="horizon"/> of the duel's last
    /// second, so the duel runs into that death's lead-in; else the solo laner; else the lower
    /// player slot.
    /// </summary>
    private static Unit Follow(
        Unit laner,
        Unit enemy,
        int last,
        int horizon,
        IReadOnlyCollection<Player> solo,
        Player[] players
    )
    {
        int lanerDies = DiesIn(laner, last, horizon);
        int enemyDies = DiesIn(enemy, last, horizon);
        if (lanerDies != enemyDies)
        {
            return lanerDies < enemyDies ? laner : enemy;
        }

        if (!solo.Contains(enemy.PlayerControlledBy))
        {
            return laner;
        }

        return Slot(laner, players) <= Slot(enemy, players) ? laner : enemy;
    }

    /// <summary>Seconds until the hero dies, or <see cref="int.MaxValue"/> beyond <paramref name="horizon"/>.</summary>
    private static int DiesIn(Unit unit, int second, int horizon)
    {
        if (!unit.TimeSpanDied.HasValue)
        {
            return int.MaxValue;
        }

        int seconds = unit.TimeSpanDied.Value.FloorSeconds() - second;
        return seconds >= 0 && seconds <= horizon ? seconds : int.MaxValue;
    }

    private static int Slot(Unit unit, Player[] players)
    {
        int index = Array.IndexOf(players, unit.PlayerControlledBy);
        return index < 0 ? int.MaxValue : index;
    }
}
