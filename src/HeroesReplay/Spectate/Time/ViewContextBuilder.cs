using Heroes.ReplayParser;

using System;
using System.Diagnostics;

namespace HeroesReplay
{
    /// <summary>
    /// A utility class used to create a view content for the spectator
    /// I don't see a reason that Minutes will need to be supported.
    /// </summary>
    public class ViewContextBuilder
    {
        private int seconds;
        private Stopwatch stopwatch;
        private Replay replay;

        public ViewContextBuilder(Stopwatch stopwatch, Replay replay)
        {
            this.stopwatch = stopwatch;
            this.replay = replay;
        }

        private ViewContextBuilder(Stopwatch stopwatch, Replay replay, int seconds)
        {
            this.seconds = seconds;
            this.replay = replay;
            this.stopwatch = stopwatch;
        }

        public ViewContextBuilder TheNext(int seconds) => new ViewContextBuilder(stopwatch, replay, seconds);

        public ViewContext Seconds => new ViewContext(stopwatch, replay, TimeSpan.FromSeconds(seconds));
    }
}
