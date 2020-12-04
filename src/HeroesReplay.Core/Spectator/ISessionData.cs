using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core
{
    public interface ISessionHolder
    {
        SessionData SessionData { get; }
        StormReplay StormReplay { get; }
    }

    public class SessionHolder : ISessionHolder, ISessionWriter
    {
        public SessionData SessionData { get; set; }
        public StormReplay StormReplay { get; set; }
    }

    public interface ISessionWriter
    {
        SessionData SessionData { set; }
        StormReplay StormReplay { set; }
    }


}