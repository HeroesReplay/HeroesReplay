using System.Threading.Tasks;

namespace HeroesReplay.Core.Twitch
{
    public interface IReplayIdRequestQueueWriter
    {
        Task AddReplayId(int replayId);
    }
}
