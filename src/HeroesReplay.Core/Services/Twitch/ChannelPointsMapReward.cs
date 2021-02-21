using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Twitch
{
    public class ChannelPointsMapReward
    {
        public string Title { get; }
        public Map Map { get; }
        public Tier? Tier { get; }

        public ChannelPointsMapReward(string title, Map map, Tier? tier)
        {
            Title = title;
            Map = map;
            Tier = tier;
        }
    }
}
