using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public interface IRequestQueue
    {
        Task<RewardQueueItem> DequeueItemAsync();
        Task<RewardResponse> EnqueueItemAsync(RewardRequest request);
        Task<int> GetItemsInQueue();
        Task<RewardQueueItem> FindByIndexAsync(int index);
        Task<(RewardQueueItem, int Position)?> FindNextByLoginAsync(string login);
    }
}