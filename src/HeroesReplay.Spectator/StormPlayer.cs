using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan When { get; }
        public TimeSpan Timer { get;  }
        public GameCriteria Criteria { get; }
        public StormPlayer(Player player, TimeSpan timer, TimeSpan when, GameCriteria criteria)
        {
            Timer = timer;
            Player = player;
            When = when;
            Criteria = criteria;
        }
    }
}