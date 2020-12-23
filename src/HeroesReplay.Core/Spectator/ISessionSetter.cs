using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface ISessionSetter
    {
        void Set(SessionData data, StormReplay replay);
    }
}