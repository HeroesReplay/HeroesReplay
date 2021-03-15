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
    public class BossCampCaptureCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;
        private readonly TrackerEventOptions trackerOptions;

        public BossCampCaptureCalculator(
            IOptions<WeightOptions> weightOptions, 
            IOptions<SpectateOptions> spectateOptions, 
            IOptions<TrackerEventOptions> trackerOptions,
            IGameData gameData)
        {
            this.gameData = gameData;
            this.trackerOptions = trackerOptions.Value;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));

            var campCaptures = replay.TrackerEvents.Where(trackerEvent => trackerEvent.TimeSpan == now &&
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
                    if (gameData.BossUnits.Contains(unit.Name))
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerKilledBy,
                        weightOptions.BossCapture,
                        $"{unit.PlayerKilledBy.Character} captured {unit.Name} (CampCaptures)");
                    }
                }
            }
        }
    }
}