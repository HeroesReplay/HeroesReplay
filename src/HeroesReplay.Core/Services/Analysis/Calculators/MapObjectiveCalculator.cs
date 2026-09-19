using System;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class MapObjectiveCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public MapObjectiveCalculator(AppSettings settings, IGameData gameData)
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

        if (timeline.Replay.TeamObjectives != null)
        {
            foreach (var teamObjectives in timeline.Replay.TeamObjectives)
            {
                foreach (TeamObjective teamObjective in teamObjectives)
                {
                    if (teamObjective.Player == null)
                    {
                        continue;
                    }

                    int second = teamObjective.TimeSpan.FloorSeconds();
                    Unit heroUnit = null;
                    foreach (
                        Unit unit in teamObjective.Player.HeroUnits
                            ?? new System.Collections.Generic.List<Unit>()
                    )
                    {
                        if (timeline.TryGetPoint(unit, second, out _))
                        {
                            heroUnit = unit;
                            break;
                        }
                    }

                    if (heroUnit == null)
                    {
                        continue;
                    }

                    timeline.Offer(
                        teamObjective.TimeSpan,
                        GetType(),
                        heroUnit,
                        teamObjective.Player,
                        settings.Weights.MapObjective,
                        $"{teamObjective.Player.Character} did {teamObjective.TeamObjectiveType} (TeamObjective)"
                    );
                }
            }
        }

        foreach (Unit mapUnit in timeline.Replay.Units)
        {
            if (!mapUnit.TimeSpanDied.HasValue || mapUnit.PlayerKilledBy == null)
            {
                continue;
            }

            if (gameData.GetUnitGroup(mapUnit.Name) != Unit.UnitGroup.MapObjective)
            {
                continue;
            }

            timeline.Offer(
                mapUnit.TimeSpanDied.Value,
                GetType(),
                mapUnit,
                mapUnit.PlayerKilledBy,
                settings.Weights.MapObjective,
                $"{mapUnit.PlayerKilledBy.Character} destroyed {mapUnit.Name} (MapObjective)"
            );
        }
    }
}
