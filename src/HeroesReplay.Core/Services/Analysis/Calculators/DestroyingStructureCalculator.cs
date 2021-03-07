using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analysis.Calculators
{
    public class DestroyingStructureCalculator : IFocusCalculator
    {
        private const string TownWallUnit = "TownWall";
        private const string TownGateUnit = "TownGate";
        private const string TownCannonUnit = "TownCannon";
        private const string TownMoonwellUnit = "TownMoonwell";
        private const string TownHallFortKeepUnit = "TownTownHall";

        private readonly IOptions<AppSettings> settings;
        private readonly IGameData gameData;

        public DestroyingStructureCalculator(IOptions<AppSettings> settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }
        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == now && gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.Structures && unit.PlayerKilledBy != null))
            {
                var weighting = unit.Name switch
                {
                    string name when name.StartsWith(TownWallUnit) => settings.Value.Weights.TownWall,
                    string name when name.StartsWith(TownGateUnit) => settings.Value.Weights.TownGate,
                    string name when name.StartsWith(TownCannonUnit) => settings.Value.Weights.TownCannon,
                    string name when name.StartsWith(TownMoonwellUnit) => settings.Value.Weights.TownMoonWell,
                    string name when name.StartsWith(TownHallFortKeepUnit) => settings.Value.Weights.TownTownHall,
                    string name when gameData.CoreUnits.Any(core => name.Equals(core)) => settings.Value.Weights.Core,
                    _ => settings.Value.Weights.Structure
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