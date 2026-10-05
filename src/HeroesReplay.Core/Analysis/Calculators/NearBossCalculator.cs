using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// Heroes at a boss (#234). A laning boss belongs to a team and is live while it walks the lane:
/// a hero near it scores <see cref="WeightSettings.ObjectiveActivity"/>. A pit boss belongs to no
/// team and only matters while it is being taken: the seconds before it dies in which the team
/// that kills it has a hero at it score <see cref="WeightSettings.BossCapture"/>. Walking past a
/// boss pit is roaming.
/// </summary>
public class NearBossCalculator : IFocusCalculator
{
    /// <summary>
    /// The longest take measured in 35 replays was 45 s (median 19 s). A hero who stood at the pit
    /// longer before that was waiting, not taking it.
    /// </summary>
    internal const int TakeMaxSeconds = 45;

    /// <summary>Seconds the taking team may step out of range and still be taking it.</summary>
    internal const int TakeGapSeconds = 3;

    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public NearBossCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        int range = settings.Spectate.MaxDistanceToBoss;
        var bosses = new List<(Unit Unit, Dictionary<int, Point> Points, HashSet<int> Take)>();
        foreach (Unit unit in timeline.Replay.Units)
        {
            if (
                gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.MercenaryCamp
                || !gameData.BossUnits.Contains(unit.Name)
            )
            {
                continue;
            }

            // A laning boss is sampled every few seconds; between samples its path is interpolated.
            Dictionary<int, Point> points = NearMapUnitCalculator.PointsFor(
                unit,
                timeline.TotalSeconds
            );
            HashSet<int> take = Laning(unit) ? null : TakeSeconds(unit, points, timeline, range);
            if (take is { Count: 0 })
            {
                continue;
            }

            bosses.Add((unit, points, take));
        }

        if (bosses.Count == 0)
        {
            return;
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            TimeSpan now = TimeSpan.FromSeconds(second);
            foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
            {
                if (
                    !timeline.TryGetPoint(heroUnit, second, out Point heroPoint)
                    || heroUnit.PlayerControlledBy == null
                )
                {
                    continue;
                }

                foreach (
                    (Unit boss, Dictionary<int, Point> bossPoints, HashSet<int> take) in bosses
                )
                {
                    bool live = take == null ? boss.IsAliveAt(now) : take.Contains(second);
                    if (!live || !bossPoints.TryGetValue(second, out Point bossPoint))
                    {
                        continue;
                    }

                    if (
                        heroPoint.DistanceTo(bossPoint) >= range
                        || FocusActivity.IdleRemoteBody(
                            timeline,
                            heroUnit,
                            heroPoint,
                            second,
                            settings.Spectate
                        )
                    )
                    {
                        continue;
                    }

                    string character = heroUnit.PlayerControlledBy.Character;
                    if (take == null)
                    {
                        timeline.Offer(
                            now,
                            GetType(),
                            heroUnit,
                            heroUnit.PlayerControlledBy,
                            settings.Weights.ObjectiveActivity,
                            $"{character} near {boss.Name} (Boss)"
                        );
                        continue;
                    }

                    string verb = heroUnit.Team == boss.PlayerKilledBy.Team ? "takes" : "contests";
                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        settings.Weights.BossCapture,
                        $"{character} {verb} {boss.Name} (Boss)"
                    );
                }
            }
        }
    }

    private static bool Laning(Unit boss) => boss.Team is 0 or 1;

    /// <summary>
    /// The seconds up to the boss's death in which a hero of the team that killed it was within
    /// <paramref name="range"/>, allowing <see cref="TakeGapSeconds"/> out of range and at most
    /// <see cref="TakeMaxSeconds"/>. Empty for a boss nobody killed.
    /// </summary>
    internal static HashSet<int> TakeSeconds(
        Unit boss,
        Dictionary<int, Point> points,
        ReplayTimeline timeline,
        int range
    )
    {
        var seconds = new HashSet<int>();
        if (!boss.TimeSpanDied.HasValue || boss.PlayerKilledBy == null || points.Count == 0)
        {
            return seconds;
        }

        int died = Math.Min(boss.TimeSpanDied.Value.FloorSeconds(), timeline.TotalSeconds - 1);
        int team = boss.PlayerKilledBy.Team;
        int start = -1;
        int gap = 0;
        for (int second = died; second >= Math.Max(0, died - TakeMaxSeconds); second--)
        {
            if (TeamAt(timeline, points, team, second, range))
            {
                start = second;
                gap = 0;
            }
            else if (++gap > TakeGapSeconds)
            {
                break;
            }
        }

        for (int second = start; start >= 0 && second <= died; second++)
        {
            seconds.Add(second);
        }

        return seconds;
    }

    private static bool TeamAt(
        ReplayTimeline timeline,
        Dictionary<int, Point> points,
        int team,
        int second,
        int range
    )
    {
        if (!points.TryGetValue(second, out Point bossPoint))
        {
            return false;
        }

        return timeline
            .AliveHeroesAt(second)
            .Any(hero =>
                hero.Team == team
                && timeline.TryGetPoint(hero, second, out Point point)
                && point.DistanceTo(bossPoint) < range
            );
    }
}
