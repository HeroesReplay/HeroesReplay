using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Providers
{
    public interface IReplayHelper
    {
        bool TryGetReplayId(string path, out int replayId);
    }
}