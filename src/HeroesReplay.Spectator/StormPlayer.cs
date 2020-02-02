using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan Duration { get; }
        public TimeSpan Timer { get;  }
        public GameEvent Event { get; }
        public StormPlayer(Player player, TimeSpan timer, TimeSpan duration, GameEvent @event)
        {
            Timer = timer;
            Player = player;
            Duration = duration;
            Event = @event;
        }
    }
}