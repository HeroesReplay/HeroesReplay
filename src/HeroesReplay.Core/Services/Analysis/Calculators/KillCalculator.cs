using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class KillCalculator : IFocusCalculator
{
    private readonly AppSettings settings;

    public KillCalculator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var killsByKiller = new Dictionary<Player, List<Unit>>();

        foreach (Unit unit in timeline.HeroUnits)
        {
            if (!unit.TimeSpanDied.HasValue || unit.PlayerKilledBy == null)
            {
                continue;
            }

            if (!killsByKiller.TryGetValue(unit.PlayerKilledBy, out List<Unit> list))
            {
                list = new List<Unit>();
                killsByKiller[unit.PlayerKilledBy] = list;
            }

            list.Add(unit);
        }

        int window = (int)settings.Spectate.KillStreakWindow.TotalSeconds;
        if (window <= 0)
        {
            window = 12;
        }

        int hold = (int)settings.Spectate.KillStreakHoldTime.TotalSeconds;
        if (hold < 0)
        {
            hold = 0;
        }

        foreach ((Player killer, List<Unit> victims) in killsByKiller)
        {
            victims.Sort(
                (left, right) => left.TimeSpanDied.Value.CompareTo(right.TimeSpanDied.Value)
            );

            var seconds = new List<int>(victims.Count);
            foreach (Unit victim in victims)
            {
                seconds.Add(victim.TimeSpanDied.Value.FloorSeconds());
            }

            IReadOnlyList<KillStreak> streaks = KillStreaks.Group(seconds, window);
            int offset = 0;
            foreach (KillStreak streak in streaks)
            {
                OfferStreak(timeline, killer, victims.GetRange(offset, streak.Kills), streak, hold);
                offset += streak.Kills;
            }
        }
    }

    private void OfferStreak(
        ReplayTimeline timeline,
        Player killer,
        List<Unit> victims,
        KillStreak streak,
        int holdSeconds
    )
    {
        float bonus = settings.Weights.KillStreakBonus;
        if (bonus <= 0)
        {
            bonus = 1;
        }

        float weight = settings.Weights.PlayerKill + (streak.Kills - 1) * bonus;
        if (streak.Kills >= 5 && settings.Weights.PentaKill > weight)
        {
            weight = settings.Weights.PentaKill;
        }

        Unit lastVictim = victims[victims.Count - 1];
        bool longRangeSolo =
            streak.Kills == 1 && IsLongRange(timeline, lastVictim, streak.EndSecond);
        Player target = longRangeSolo ? lastVictim.PlayerControlledBy : killer;
        if (target == null)
        {
            return;
        }

        Unit focusUnit = longRangeSolo
            ? lastVictim
            : KillerHero(timeline, killer, streak.StartSecond) ?? lastVictim;

        string description = Describe(killer, victims, streak.Kills);

        if (streak.Kills == 1)
        {
            timeline.Offer(
                lastVictim.TimeSpanDied.Value,
                GetType(),
                focusUnit,
                target,
                weight,
                description
            );
            return;
        }

        TimeSpan start = TimeSpan.FromSeconds(streak.StartSecond);
        TimeSpan end = TimeSpan.FromSeconds(streak.EndSecond + holdSeconds);
        timeline.OfferRange(start, end, GetType(), focusUnit, target, weight, description);
    }

    private bool IsLongRange(ReplayTimeline timeline, Unit victim, int second)
    {
        if (victim.PlayerKilledBy?.HeroUnits == null)
        {
            return false;
        }

        foreach (Unit killerUnit in victim.PlayerKilledBy.HeroUnits)
        {
            if (!timeline.TryGetPoint(killerUnit, second, out Point killerPoint))
            {
                continue;
            }

            if (killerPoint.DistanceTo(victim.PointDied) > settings.Spectate.MaxDistanceToEnemyKill)
            {
                return true;
            }
        }

        return false;
    }

    private static Unit KillerHero(ReplayTimeline timeline, Player killer, int second)
    {
        if (killer.HeroUnits == null)
        {
            return null;
        }

        foreach (Unit unit in killer.HeroUnits)
        {
            if (timeline.TryGetPoint(unit, second, out _))
            {
                return unit;
            }
        }

        return killer.HeroUnits.Count > 0 ? killer.HeroUnits[0] : null;
    }

    private static string Describe(Player killer, List<Unit> victims, int kills)
    {
        string hero = killer.Character;
        return kills switch
        {
            1 => $"{hero} kills {victims[0].PlayerControlledBy?.Character}",
            2 => $"{hero} double kill",
            3 => $"{hero} triple kill",
            4 => $"{hero} quadra kill",
            _ => UniqueEnemyCount(victims) >= 5
                ? $"{hero} pentakill (team wipe)"
                : $"{hero} pentakill",
        };
    }

    private static int UniqueEnemyCount(List<Unit> victims)
    {
        var seen = new HashSet<Player>();
        foreach (Unit victim in victims)
        {
            if (victim.PlayerControlledBy != null)
            {
                seen.Add(victim.PlayerControlledBy);
            }
        }

        return seen.Count;
    }
}
