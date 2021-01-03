using Heroes.ReplayParser;

namespace HeroesReplay.Core.Models
{
    /// <summary>
    /// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class StormReplay
    {
        public string Path { get; }

        public int? ReplayId { get; }

        public string GameType { get; }

        public Replay Replay { get; }

        public StormReplay(string path, Replay replay, int? replayId, string gameType)
        {
            Replay = replay;
            Path = path;
            ReplayId = replayId;
            GameType = gameType;
        }
    }
}