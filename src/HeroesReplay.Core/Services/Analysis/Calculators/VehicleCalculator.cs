using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class VehicleCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public VehicleCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(unit => gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MapObjective && gameData.VehicleUnits.Contains(unit.Name)))
            {
                if (!unit.Positions.Any()) continue;

                if (unit.PlayerControlledBy != null)
                {
                    var startTime = unit.Positions.Select(p => p.TimeSpan).Min();
                    var endTime = unit.TimeSpanDied ?? unit.Positions.Select(p => p.TimeSpan).Max();

                    if (startTime <= now && now <= endTime)
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerControlledBy,
                        settings.Weights.MapObjective,
                        $"{unit.PlayerControlledBy.Character} is inside {unit.Name} (MapObjective).");
                    }
                }
                else if (unit.OwnerChangeEvents != null && unit.OwnerChangeEvents.Any(e => e.PlayerNewOwner != null))
                {
                    foreach (OwnerChangeEvent currentEvent in unit.OwnerChangeEvents.Where(x => x.PlayerNewOwner != null))
                    {
                        TimeSpan startTime = currentEvent.TimeSpanOwnerChanged;
                        Player target = currentEvent.PlayerNewOwner;
                        TimeSpan endTime = unit.TimeSpanDied ?? unit.Positions.Select(p => p.TimeSpan).Max();
                        OwnerChangeEvent exitEvent = null;

                        int startIndex = unit.OwnerChangeEvents.IndexOf(currentEvent);
                        int exitIndex = startIndex + 1;

                        bool tryFindExitEvent = unit.OwnerChangeEvents.Count - 1 >= exitIndex;

                        if (tryFindExitEvent)
                        {
                            exitEvent = unit.OwnerChangeEvents.ElementAt(exitIndex);
                            endTime = exitEvent.TimeSpanOwnerChanged;
                        }

                        if (now >= startTime && now <= endTime)
                        {
                            yield return new Focus(
                            calculator: GetType(),
                                unit: unit,
                                target: target,
                                points: settings.Weights.MapObjective,
                                description: $"{target.Character} is inside {unit.Name} (MapObjective) [{unit.OwnerChangeEvents.IndexOf(currentEvent)}]");
                        }
                    }
                }
            }
        }
    }
}