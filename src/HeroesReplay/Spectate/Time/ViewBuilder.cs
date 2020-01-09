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
        private GameSpectator spectator;

        public ViewSpan Seconds()
        {
            var now = spectator.Timer;
            return new ViewSpan(spectator.Game.Replay, now, now.Add(TimeSpan.FromSeconds(seconds)));
        }
        
        public ViewBuilder(GameSpectator spectator)
        {   
            this.spectator = spectator;
        }

        private ViewBuilder(GameSpectator spectator, int seconds)
        {
            this.seconds = seconds;
            this.spectator = spectator;
        }

        public ViewBuilder TheNext(int seconds) => new ViewBuilder(spectator, seconds);
    }
}