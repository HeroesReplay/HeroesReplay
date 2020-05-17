using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan Duration { get; }
        public TimeSpan Timer { get; }
        public SpectateEvent SpectateEvent { get; }

        public StormPlayer(Player player, TimeSpan timer, TimeSpan duration, SpectateEvent spectateEvent)
        {
            Timer = timer;
            Player = player;
            Duration = duration;
            SpectateEvent = spectateEvent;
        }

        public override string ToString() => $"hero: {Player?.HeroId}, event: {SpectateEvent}, timer: {Timer}, duration: {Duration}";
    }
}