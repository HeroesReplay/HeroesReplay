using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Context
{
    public interface IReplayContext
    {
        ContextData Previous { get; set; }
        ContextData Current { get; set; }
    }
}