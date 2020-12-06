using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class DestroyingStructureCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public DestroyingStructureCalculator(Settings settings)
        {
            this.settings = settings;
        }
        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == now && unit.Name.StartsWith("Town") && unit.PlayerKilledBy != null))
            {
                var points = unit.Name switch
                {
                    string name when name.StartsWith("TownWall") => 200,
                    string name when name.StartsWith("TownGate") => 400,
                    string name when name.StartsWith("TownCannon") => 600,
                    string name when name.StartsWith("TownMoonwell") => 800,
                    string name when name.StartsWith("TownTownHall") => 1000,
                };

                yield return new Focus(GetType(), unit, unit.PlayerKilledBy, settings.Weights.DestroyStructure + points, $"{unit.PlayerKilledBy.HeroId} destroyed {unit.Name}");
            }
        }
    }
}