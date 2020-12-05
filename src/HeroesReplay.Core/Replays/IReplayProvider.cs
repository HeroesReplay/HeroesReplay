using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Replays
{
    public interface IReplayProvider
    {
        Task<StormReplay?> TryLoadReplayAsync();
    }
}