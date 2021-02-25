using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer
{
    public interface IReplayContext
    {
        SessionData Previous { get; }
        SessionData Current { get; }
    }
}