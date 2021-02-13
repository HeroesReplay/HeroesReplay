using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface ISessionSetter
    {
        void SetSession(SessionData data);
    }
}