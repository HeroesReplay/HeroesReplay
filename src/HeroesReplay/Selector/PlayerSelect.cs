using System;
using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace HeroesReplay.Selector
{
    public class PlayerSelect
    {
        public Player Player { get; }
        public TimeSpan Period { get; }
        public SelectorReason Reason { get; }

        public PlayerSelect(Player player, TimeSpan period, SelectorReason reason)
        {
            Player = player;
            Period = period;
            Reason = reason;
        }

        public async Task WatchAsync() => await Task.Delay(Period);
    }
}