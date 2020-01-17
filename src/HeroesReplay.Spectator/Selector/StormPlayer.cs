using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan When { get; }
        public SelectorCriteria Criteria { get; }
        public TimeSpan GetDuration(TimeSpan now) => now - When;
        public StormPlayer(Player player, TimeSpan when, SelectorCriteria criteria)
        {
            Player = player;
            When = when;
            Criteria = criteria;
        }
    }
}