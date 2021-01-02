using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface ISessionHolder
    {
        SessionData SessionData { get; }
        StormReplay StormReplay { get; }
    }
}