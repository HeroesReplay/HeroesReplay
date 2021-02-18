namespace HeroesReplay.Core.Twitch
{
    public interface IReplayIdRequestQueueReader
    {
        int? GetNextReplayId();
    }
}
