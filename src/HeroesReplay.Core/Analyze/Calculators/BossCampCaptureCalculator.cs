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
    public class BossCampCaptureCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public BossCampCaptureCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }
        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            var campCaptures = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan == now &&
                                                                    trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                                                                    trackerEvent.Data.dictionary[0].blobText == "JungleCampCapture");

            foreach (TrackerEvent capture in campCaptures)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.GetValueOrDefault() - 1;

                foreach (Unit unit in replay.Units.Where(unit => unit.TimeSpanDied < capture.TimeSpan && 
                                                                 unit.TimeSpanBorn < capture.TimeSpan && 
                                                                 unit.PlayerKilledBy != null && 
                                                                 unit.PlayerKilledBy.Team == teamId && 
                                                                 unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10))))
                {
                    if (gameData.BossUnits.Contains(unit.Name))
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerKilledBy,
                        settings.Weights.BossCapture,
                        $"{unit.PlayerKilledBy.Character} captured {unit.Name} (CampCaptures)");
                    }
                }
            }
        }
    }
}