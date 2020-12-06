using HeroesReplay.Core.Shared;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Replays
{
    public interface IReplayProvider
    {
        Task<StormReplay?> TryLoadReplayAsync();
    }
}