using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface IReplayRequestQueue
    {
        Task<ReplayRequest> GetNextRequestAsync();
        Task EnqueueRequestAsync(ReplayRequest request);
    }
}
