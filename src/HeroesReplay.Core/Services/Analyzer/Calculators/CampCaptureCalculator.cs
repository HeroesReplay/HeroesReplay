using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class CampCaptureCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;
        private readonly TrackerEventOptions trackerOptions;

        public CampCaptureCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IOptions<TrackerEventOptions> trackerOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
            this.trackerOptions = trackerOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var campCaptures = replay.TrackerEvents.Where(trackerEvent => (trackerEvent.TimeSpan == now || 
                                                                            (trackerEvent.TimeSpan.Add(TimeSpan.FromSeconds(1)) > now && 
                                                                             trackerEvent.TimeSpan.Subtract(TimeSpan.FromSeconds(1)) < now)
                                                                          ) &&
                                                                    trackerEvent.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                                                                    trackerEvent.Data.dictionary[0].blobText == trackerOptions.JungleCampCapture);

            foreach (TrackerEvent capture in campCaptures)
            {
                int teamId = (int)capture.Data.dictionary[3].optionalData.array[0].dictionary[1].vInt.GetValueOrDefault() - 1;

                foreach (Unit unit in replay.Units.Where(unit => unit.TimeSpanDied < capture.TimeSpan &&
                                                                 unit.TimeSpanBorn < capture.TimeSpan &&
                                                                 unit.PlayerKilledBy != null &&
                                                                 unit.PlayerKilledBy.Team == teamId &&
                                                                 unit.TimeSpanDied > capture.TimeSpan.Subtract(TimeSpan.FromSeconds(10))))
                {
                    var standardCamp = !gameData.BossUnits.Contains(unit.Name);

                    if (standardCamp)
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerKilledBy,
                        weightOptions.CampCapture,
                        $"{unit.PlayerKilledBy.Character} captured {unit.Name} (CampCaptures)");
                    }
                }
            }
        }
    }
}