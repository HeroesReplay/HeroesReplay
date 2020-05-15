using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Picker
{
    public class ReplayPicker
    {
        private readonly StormPlayerTool stormPlayertool;

        public ReplayPicker(StormPlayerTool stormPlayertool)
        {
            this.stormPlayertool = stormPlayertool;
        }

        public bool IsInteresting(StormReplay stormReplay)
        {
            double totalSeconds = stormReplay.Replay.ReplayLength.TotalSeconds;
            List<TimeSpan> timeSlices = new List<TimeSpan>();

            for (int seconds = 0; seconds < totalSeconds && seconds + 48 < totalSeconds; seconds += 48)
            {
                timeSlices.Add(TimeSpan.FromSeconds(seconds));
            }

            // close core kills
            // penta, quad or triple Kills
            // boss or camp steals
            // Deaths near bosses or objectives

            return timeSlices
                .AsParallel()
                .SelectMany(timeSlice => stormPlayertool.GetPlayers(stormReplay.Replay, timeSlice))
                .Any(result => TimeSliceContains(result));
        }

        private bool TimeSliceContains(StormPlayer result)
        {
            return result.SpectateEvent == SpectateEvent.QuintupleKill ||
                   result.SpectateEvent == SpectateEvent.QuadKill ||
                   result.SpectateEvent == SpectateEvent.TripleKill ||
                   result.SpectateEvent == SpectateEvent.Taunt;
        }
    }
}
