using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan When { get; }
        public SelectorReason Reason { get; }
        public TimeSpan GetDuration(TimeSpan now) => now - When;
        public StormPlayer(Player player, TimeSpan when, SelectorReason reason)
        {
            Player = player;
            When = when;
            Reason = reason;
        }
    }
}