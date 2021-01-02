using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public class SessionHolder : ISessionHolder, ISessionSetter
    {
        public SessionData SessionData { get; private set; }
        public StormReplay StormReplay { get; private set; }

        public void SetSession(SessionData data, StormReplay replay)
        {
            this.SessionData = data;
            this.StormReplay = replay;
        }
    }
}