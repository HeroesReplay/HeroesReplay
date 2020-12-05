using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class CampCaptureWeightings : IGameWeightings
    {
        private readonly Settings settings;

        public CampCaptureWeightings(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            yield break;

            var events = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan == now && trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in events)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.Value - 1;

                foreach (Unit unit in replay.Units.Where(unit => unit.TimeSpanBorn < capture.TimeSpan && unit.TimeSpanDied < capture.TimeSpan && unit.PlayerKilledBy != null && unit.PlayerKilledBy.Team == teamId && settings.UnitSettings.CampNames.Any(oc => unit.Name.Contains(oc))))
                {
                    yield return (unit, unit.PlayerKilledBy, settings.SpectateWeightSettings.CampCapture, $"{unit.PlayerKilledBy.HeroId} captured {unit.Name} (CampCaptures)");
                }
            }
        }
    }
}