using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public class StormPlayer
    {
        public Player Player { get; }
        public TimeSpan When { get; }
        public GameCriteria Criteria { get; }
        public StormPlayer(Player player, TimeSpan when, GameCriteria criteria)
        {
            Player = player;
            When = when;
            Criteria = criteria;
        }
    }
}