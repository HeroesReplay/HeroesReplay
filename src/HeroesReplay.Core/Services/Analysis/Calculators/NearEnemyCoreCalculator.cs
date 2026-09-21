using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class NearEnemyCoreCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public NearEnemyCoreCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings;
        this.gameData = gameData;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var cores = new List<Unit>();
        foreach (Unit unit in timeline.Replay.Units ?? new List<Unit>())
        {
            if (IsCore(unit))
            {
                cores.Add(unit);
            }
        }

        if (cores.Count == 0)
        {
            return;
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            TimeSpan now = TimeSpan.FromSeconds(second);
            foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
            {
                if (
                    !timeline.TryGetPoint(heroUnit, second, out Point point)
                    || heroUnit.PlayerControlledBy == null
                )
                {
                    continue;
                }

                foreach (Unit core in cores)
                {
                    if (core.Team == heroUnit.Team || !core.IsAliveAt(now))
                    {
                        continue;
                    }

                    if (
                        core.PointBorn == null
                        || point.DistanceTo(core.PointBorn) > settings.Spectate.MaxDistanceToCore
                    )
                    {
                        continue;
                    }

                    bool ending = IsEndingPush(core, now);
                    float weight = ending
                        ? settings.Weights.EndingCore
                        : settings.Weights.NearEnemyCore;
                    string kind = ending ? "ending on enemy core" : "near enemy core";
                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        weight,
                        $"{heroUnit.PlayerControlledBy.Character} {kind}: {core.Name}."
                    );
                }
            }
        }
    }

    private bool IsCore(Unit unit)
    {
        if (unit == null || string.IsNullOrWhiteSpace(unit.Name))
        {
            return false;
        }

        if (gameData?.CoreUnits != null)
        {
            foreach (string core in gameData.CoreUnits)
            {
                if (unit.Name.Equals(core, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (settings.FocusUnits?.CoreContains == null)
        {
            return false;
        }

        foreach (string token in settings.FocusUnits.CoreContains)
        {
            if (
                !string.IsNullOrWhiteSpace(token)
                && unit.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool IsEndingPush(Unit core, TimeSpan now)
    {
        if (
            !core.TimeSpanDied.HasValue
            || settings.Spectate.EndingCoreWindow <= TimeSpan.Zero
            || settings.Weights.EndingCore <= settings.Weights.NearEnemyCore
        )
        {
            return false;
        }

        TimeSpan start = core.TimeSpanDied.Value - settings.Spectate.EndingCoreWindow;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        return now >= start && core.IsAliveAt(now);
    }
}
