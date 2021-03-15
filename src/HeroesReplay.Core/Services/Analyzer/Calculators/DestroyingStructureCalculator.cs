using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class DestroyingStructureCalculator : IFocusCalculator
    {
        private const string TownWallUnit = "TownWall";
        private const string TownGateUnit = "TownGate";
        private const string TownCannonUnit = "TownCannon";
        private const string TownMoonwellUnit = "TownMoonwell";
        private const string TownHallFortKeepUnit = "TownTownHall";

        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public DestroyingStructureCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == now && gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.Structures && unit.PlayerKilledBy != null))
            {
                var weighting = unit.Name switch
                {
                    string name when name.StartsWith(TownWallUnit) => weightOptions.TownWall,
                    string name when name.StartsWith(TownGateUnit) => weightOptions.TownGate,
                    string name when name.StartsWith(TownCannonUnit) => weightOptions.TownCannon,
                    string name when name.StartsWith(TownMoonwellUnit) => weightOptions.TownMoonWell,
                    string name when name.StartsWith(TownHallFortKeepUnit) => weightOptions.TownTownHall,
                    string name when gameData.CoreUnits.Any(core => name.Equals(core)) => weightOptions.Core,
                    _ => weightOptions.Structure
                };

                yield return new Focus(
                    GetType(), 
                    unit,
                    unit.PlayerKilledBy,
                    weighting, 
                    $"{unit.PlayerKilledBy.Character} destroyed {unit.Name}");
            }
        }
    }
}