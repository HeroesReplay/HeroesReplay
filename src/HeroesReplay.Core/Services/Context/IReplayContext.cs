using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Context
{
    public interface IReplayContext
    {
        ContextData Previous { get; }
        ContextData Current { get; }
    }
}