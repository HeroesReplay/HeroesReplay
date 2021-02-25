using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
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