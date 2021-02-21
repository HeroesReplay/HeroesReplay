using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public class SessionHolder : ISessionHolder, ISessionSetter
    {
        public SessionData Previous { get; private set; }
        public SessionData Current { get; private set; }

        public void SetSession(SessionData sessionData)
        {
            Previous = Current;
            Current = sessionData;
        }
    }
}