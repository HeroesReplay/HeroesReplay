using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
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
        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == now && gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.Structures && unit.PlayerKilledBy != null))
            {
                var points = unit.Name switch
                {
                    string name when name.StartsWith(TownWallUnit) => settings.Weights.TownWall,
                    string name when name.StartsWith(TownGateUnit) => settings.Weights.TownGate,
                    string name when name.StartsWith(TownCannonUnit) => settings.Weights.TownCannon,
                    string name when name.StartsWith(TownMoonwellUnit) => settings.Weights.TownMoonWell,
                    string name when name.StartsWith(TownHallFortKeepUnit) => settings.Weights.TownTownHall,
                    string name when gameData.CoreUnits.Any(core => name.Equals(core)) => settings.Weights.Core,
                    _ => settings.Weights.Structure
                };

                yield return new Focus(GetType(), unit, unit.PlayerKilledBy, points, $"{unit.PlayerKilledBy.HeroId} destroyed {unit.Name}");
            }
        }
    }
}