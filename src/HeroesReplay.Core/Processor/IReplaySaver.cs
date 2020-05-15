using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Replays
{
    public interface IReplaySaver
    {
        Task<StormReplay> SaveReplayAsync(StormReplay stormReplay);
    }
}