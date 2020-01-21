using System.Threading.Tasks;
using HeroesReplay.Shared;

namespace HeroesReplay.Replays
{
    public interface IStormReplayProvider
    {
        Task<StormReplay> TryLoadReplayAsync();
    }
}