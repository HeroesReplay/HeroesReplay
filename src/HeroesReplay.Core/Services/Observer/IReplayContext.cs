using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface IReplayContext
    {
        SessionData Previous { get; }
        SessionData Current { get; }
    }
}