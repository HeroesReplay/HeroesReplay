using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Queue
{
    public interface IRequestQueueDequeuer
    {
        Task<RewardQueueItem> DequeueItemAsync();
    }
}