using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Models
{
    public class RewardQueueItem
    {
        public RewardRequest Request { get; }
        public RewardReplay Replay { get; }

        public RewardQueueItem(RewardRequest request, RewardReplay replay)
        {
            Replay = replay;
            Request = request;
        }
    }
}
