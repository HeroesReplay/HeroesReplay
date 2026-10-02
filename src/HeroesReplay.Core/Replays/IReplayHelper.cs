namespace HeroesReplay.Core.Replays;

public interface IReplayHelper
{
    bool TryGetReplayId(string path, out int replayId);
}
