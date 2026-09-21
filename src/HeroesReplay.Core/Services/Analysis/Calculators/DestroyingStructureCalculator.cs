using System;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class DestroyingStructureCalculator : IFocusCalculator
{
    private const string TownWallUnit = "TownWall";
    private const string TownGateUnit = "TownGate";
    private const string TownCannonUnit = "TownCannon";
    private const string TownMoonwellUnit = "TownMoonwell";
    private const string TownHallFortKeepUnit = "TownTownHall";

    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public DestroyingStructureCalculator(AppSettings settings, IGameData gameData)
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
                unit.TimeSpanBorn != TimeSpan.Zero
                || !unit.TimeSpanDied.HasValue
                || unit.PlayerKilledBy == null
            )
            {
                continue;
            }

            bool core = IsCore(unit.Name);
            if (!core && gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.Structures)
            {
                continue;
            }

            float weighting = unit.Name switch
            {
                string name when core => settings.Weights.Core,
                string name when name.StartsWith(TownWallUnit) => settings.Weights.TownWall,
                string name when name.StartsWith(TownGateUnit) => settings.Weights.TownGate,
                string name when name.StartsWith(TownCannonUnit) => settings.Weights.TownCannon,
                string name when name.StartsWith(TownMoonwellUnit) => settings.Weights.TownMoonWell,
                string name when name.StartsWith(TownHallFortKeepUnit) => settings
                    .Weights
                    .TownTownHall,
                _ => settings.Weights.Structure,
            };

            timeline.Offer(
                unit.TimeSpanDied.Value,
                GetType(),
                unit,
                unit.PlayerKilledBy,
                weighting,
                $"{unit.PlayerKilledBy.Character} destroyed {unit.Name}"
            );
        }
    }

    private bool IsCore(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (gameData?.CoreUnits != null)
        {
            foreach (string core in gameData.CoreUnits)
            {
                if (name.Equals(core, StringComparison.OrdinalIgnoreCase))
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
                && name.Contains(token, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}
