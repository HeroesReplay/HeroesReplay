using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class DestroyStructureWeightings : IGameWeightings
    {
        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
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

                yield return (unit, unit.PlayerKilledBy, Constants.Weights.DestroyStructure + points, $"{unit.PlayerKilledBy.HeroId} destroyed {unit.Name}");
            }
        }
    }
}