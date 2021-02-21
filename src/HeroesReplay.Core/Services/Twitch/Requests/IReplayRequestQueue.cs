using HeroesReplay.Core.Models;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface IReplayRequestQueue
    {
        Task<RewardQueueItem> GetNextRewardQueueItem();
        Task<RewardResponse> EnqueueRequestAsync(RewardRequest request);
        Task<int> GetTotalQueuedItems();
    }
}