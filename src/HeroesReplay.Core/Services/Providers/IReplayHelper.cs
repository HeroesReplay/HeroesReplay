namespace HeroesReplay.Core.Services.Providers
{
    public interface IReplayHelper
    {
        bool TryGetReplayId(string path, out int replayId);
    }
}