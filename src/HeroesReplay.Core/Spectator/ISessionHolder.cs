using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface ISessionHolder
    {
        SessionData SessionData { get; }
        StormReplay StormReplay { get; }
    }
}