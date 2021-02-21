using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface ISessionHolder
    {
        SessionData Previous { get; }
        SessionData Current { get; }
    }
}