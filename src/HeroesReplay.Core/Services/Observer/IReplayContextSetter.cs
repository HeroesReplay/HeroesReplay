using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface IReplayContextSetter
    {
        void SetContext(LoadedReplay stormReplay);
    }
}