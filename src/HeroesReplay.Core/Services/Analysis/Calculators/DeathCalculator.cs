using System;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class DeathCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public DeathCalculator(AppSettings settings, IGameData gameData)
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
            if (!unit.TimeSpanDied.HasValue || unit.PlayerControlledBy == null)
            {
                continue;
            }

            if (gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.Hero)
            {
                continue;
            }

            if (unit.PlayerKilledBy != null && unit.PlayerKilledBy != unit.PlayerControlledBy)
            {
                continue;
            }

            timeline.Offer(
                unit.TimeSpanDied.Value,
                GetType(),
                unit,
                unit.PlayerControlledBy,
                settings.Weights.PlayerDeath,
                $"{unit.PlayerControlledBy.Character} killed by {unit.UnitKilledBy?.Name}"
            );
        }
    }
}
