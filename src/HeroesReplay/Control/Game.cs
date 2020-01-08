using Heroes.ReplayParser;
using System.IO;

namespace HeroesReplay
{
    /// <summary>
    /// The Game is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class Game
    {
        public string FilePath { get; }

        public string Name => Path.GetFileName(FilePath);

        public Replay Replay { get; }

        public Game(string path, Replay replay)
        {
            Replay = replay;
            FilePath = path;
        }
    }
}