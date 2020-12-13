using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class CampCaptureCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData heroesData;

        public CampCaptureCalculator(Settings settings, IGameData heroesData)
        {
            this.settings = settings;
            this.heroesData = heroesData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            yield break;

            var events = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan == now && trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in events)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;
                IEnumerable<Unit> mercenaries = replay.Units.Where(unit => heroesData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MercenaryCamp);
                IEnumerable<Unit> captured = mercenaries.Where(unit => unit.TimeSpanBorn < capture.TimeSpan && unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10)) && unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId);

                foreach (Unit unit in captured)
                {
                    yield return new Focus(GetType(), unit, unit.PlayerKilledBy, settings.Weights.CampCapture, $"{unit.PlayerKilledBy.HeroId} captured {unit.Name} (CampCaptures)");
                }
            }
        }
    }
}