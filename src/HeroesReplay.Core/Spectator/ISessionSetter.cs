using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface ISessionSetter
    {
        void SetSession(SessionData data, StormReplay replay);
    }
}