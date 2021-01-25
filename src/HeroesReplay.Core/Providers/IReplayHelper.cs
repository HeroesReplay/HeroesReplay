using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Providers
{
    public interface IReplayHelper
    {
        bool TryGetGameType(StormReplay stormReplay, out string gameType);
        bool TryGetGameType(string path, out string gameType);
        bool TryGetReplayId(StormReplay stormReplay, out int replayId);
        bool TryGetReplayId(string path, out int replayId);
    }
}