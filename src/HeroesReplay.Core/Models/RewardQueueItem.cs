using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Models
{
    public class RewardQueueItem
    {
        public RewardRequest Request { get; }
        public HeroesProfileReplay HeroesProfileReplay { get; }

        public RewardQueueItem(RewardRequest request, HeroesProfileReplay replay)
        {
            HeroesProfileReplay = replay;
            Request = request;
        }
    }
}
