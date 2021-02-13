using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public class SessionHolder : ISessionHolder, ISessionSetter
    {
        public SessionData SessionData { get; private set; }

        public void SetSession(SessionData sessionData)
        {
            SessionData = sessionData;
        }
    }
}