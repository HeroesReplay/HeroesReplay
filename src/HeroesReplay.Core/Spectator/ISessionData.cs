using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface ISessionHolder
    {
        SessionData SessionData { get; }
        StormReplay StormReplay { get; }
    }

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

    public interface ISessionSetter
    {
        void Set(SessionData data, StormReplay replay);
    }


}