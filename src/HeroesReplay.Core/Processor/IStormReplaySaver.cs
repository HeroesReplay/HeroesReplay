using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Replays
{
    public interface IStormReplaySaver
    {
        Task<StormReplay> SaveReplayAsync(StormReplay stormReplay);
    }
}