using Heroes.ReplayParser;

namespace HeroesReplay
{
    /// <summary>
    /// The Game is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class Game
    {
        public Replay Replay { get; }
        public string Path { get; }

        public Game(string path, Replay replay)
        {
            Replay = replay;
            Path = path;
        }
    }
}