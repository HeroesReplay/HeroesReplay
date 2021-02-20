using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface IReplayRequestQueue
    {
        Task<ReplayRequest> GetNextRequestAsync();
        Task<ReplayRequestResponse> EnqueueRequestAsync(ReplayRequest request);
        Task<int> GetTotalQueuedItems();
    }
}
