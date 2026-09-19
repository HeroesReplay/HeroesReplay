using System;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class VehicleCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public VehicleCalculator(AppSettings settings, IGameData gameData)
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

        foreach (Unit unit in timeline.Replay.Units)
        {
            if (
                gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.MapObjective
                || !gameData.VehicleUnits.Contains(unit.Name)
            )
            {
                continue;
            }

            if (unit.Positions == null || unit.Positions.Count == 0)
            {
                continue;
            }

            if (unit.PlayerControlledBy != null)
            {
                TimeSpan startTime = unit.Positions.Min(p => p.TimeSpan);
                TimeSpan endTime = unit.TimeSpanDied ?? unit.Positions.Max(p => p.TimeSpan);
                OfferRange(
                    timeline,
                    unit,
                    unit.PlayerControlledBy,
                    startTime,
                    endTime,
                    $"{unit.PlayerControlledBy.Character} is inside {unit.Name} (MapObjective)."
                );
                continue;
            }

            if (unit.OwnerChangeEvents == null)
            {
                continue;
            }

            for (int i = 0; i < unit.OwnerChangeEvents.Count; i++)
            {
                OwnerChangeEvent currentEvent = unit.OwnerChangeEvents[i];
                if (currentEvent.PlayerNewOwner == null)
                {
                    continue;
                }

                TimeSpan startTime = currentEvent.TimeSpanOwnerChanged;
                TimeSpan endTime = unit.TimeSpanDied ?? unit.Positions.Max(p => p.TimeSpan);
                if (i + 1 < unit.OwnerChangeEvents.Count)
                {
                    endTime = unit.OwnerChangeEvents[i + 1].TimeSpanOwnerChanged;
                }

                OfferRange(
                    timeline,
                    unit,
                    currentEvent.PlayerNewOwner,
                    startTime,
                    endTime,
                    $"{currentEvent.PlayerNewOwner.Character} is inside {unit.Name} (MapObjective) [{i}]"
                );
            }
        }
    }

    private void OfferRange(
        ReplayTimeline timeline,
        Unit unit,
        Player target,
        TimeSpan startTime,
        TimeSpan endTime,
        string description
    )
    {
        int start = startTime.FloorSeconds();
        int end = endTime.FloorSeconds();
        for (int second = start; second <= end && second < timeline.TotalSeconds; second++)
        {
            timeline.Offer(
                TimeSpan.FromSeconds(second),
                GetType(),
                unit,
                target,
                settings.Weights.MapObjective,
                description
            );
        }
    }
}
