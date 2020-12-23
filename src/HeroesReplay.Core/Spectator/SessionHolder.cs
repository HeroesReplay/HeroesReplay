using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public class SessionHolder : ISessionHolder, ISessionSetter
    {
        public SessionData SessionData { get; private set; }
        public StormReplay StormReplay { get; private set; }

        public void Set(SessionData data, StormReplay replay)
        {
            this.SessionData = data;
            this.StormReplay = replay;
        }
    }


}