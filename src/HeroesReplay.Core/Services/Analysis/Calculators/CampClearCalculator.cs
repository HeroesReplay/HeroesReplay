using System;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class CampClearCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public CampClearCalculator(AppSettings settings, IGameData gameData)
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

        foreach (Unit unit in timeline.Replay.Units)
        {
            if (!unit.TimeSpanDied.HasValue || unit.PlayerKilledBy == null)
            {
                continue;
            }

            if (gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.MercenaryCamp)
            {
                continue;
            }

            int second = unit.TimeSpanDied.Value.FloorSeconds();
            bool near = false;
            foreach (
                Unit heroUnit in unit.PlayerKilledBy.HeroUnits
                    ?? new System.Collections.Generic.List<Unit>()
            )
            {
                if (!timeline.TryGetPoint(heroUnit, second, out Point point))
                {
                    continue;
                }

                if (point.DistanceTo(unit.PointDied) < settings.Spectate.MaxDistanceToClear)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                continue;
            }

            timeline.Offer(
                unit.TimeSpanDied.Value,
                GetType(),
                unit,
                unit.PlayerKilledBy,
                settings.Weights.CampClear,
                $"{unit.PlayerKilledBy.Character} kills {unit.Name}"
            );
        }
    }
}
