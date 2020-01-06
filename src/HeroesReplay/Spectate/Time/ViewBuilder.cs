using Heroes.ReplayParser;
using System;
using System.Diagnostics;

namespace HeroesReplay
{
    /// <summary>
    /// A utility class used to create a 'context' for the spectator at a given time. 
    /// </summary>
    /// <remarks>
    /// I don't see a reason that Minutes will need to be supported.
    /// </remarks>
    public class ViewBuilder
    {
        private readonly int seconds;
        private readonly Stopwatch stopwatch;
        private readonly Replay replay;

        public ViewBuilder(Stopwatch stopwatch, Replay replay)
        {
            this.stopwatch = stopwatch;
            this.replay = replay;
        }

        private ViewBuilder(Stopwatch stopwatch, Replay replay, int seconds)
        {
            this.seconds = seconds;
            this.replay = replay;
            this.stopwatch = stopwatch;
        }

        public ViewBuilder TheNext(int seconds) => new ViewBuilder(stopwatch, replay, seconds);

        public ViewSpan Seconds => new ViewSpan(stopwatch, replay, TimeSpan.FromSeconds(seconds));
    }
}
