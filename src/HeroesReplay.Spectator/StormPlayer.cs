using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan When { get; }
        public TimeSpan Timer { get;  }
        public GameEvent Event { get; }
        public StormPlayer(Player player, TimeSpan timer, TimeSpan when, GameEvent @event)
        {
            Timer = timer;
            Player = player;
            When = when;
            Event = @event;
        }
    }
}