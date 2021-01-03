using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class CampCaptureCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public CampCaptureCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var events = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan == now && 
                                                                    trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && 
                                                                    trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in events)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.GetValueOrDefault() - 1;
                IEnumerable<Unit> mercenaries = replay.Units.Where(unit => gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MercenaryCamp);
                IEnumerable<Unit> captured = mercenaries.Where(unit => unit.TimeSpanBorn < capture.TimeSpan && unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10)) && unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);

                foreach (Unit unit in captured)
                {
                    var standardCamp = !gameData.BossUnits.Contains(unit.Name);

                    if (standardCamp)
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerKilledBy,
                        settings.Weights.CampCapture,
                        $"{unit.PlayerKilledBy.Character} captured {unit.Name} (CampCaptures)");
                    }
                }
            }
        }
    }
}