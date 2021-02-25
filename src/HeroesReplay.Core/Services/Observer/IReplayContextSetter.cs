using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer
{
    public interface IReplayContextSetter
    {
        void SetContext(LoadedReplay stormReplay);
    }
}