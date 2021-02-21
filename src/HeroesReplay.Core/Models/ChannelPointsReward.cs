using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Twitch
{
    public class ChannelPointsReward
    {
        public string Title { get; }
        public string Map { get; }
        public Tier? Tier { get; }
        public GameMode? Mode { get; }

        public ChannelPointsReward(string title, string map, Tier? tier, GameMode? mode)
        {
            Title = title;
            Map = map;
            Tier = tier;
            Mode = mode;
        }
    }
}
