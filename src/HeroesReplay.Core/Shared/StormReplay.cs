using Heroes.ReplayParser;

namespace HeroesReplay.Core.Shared
{
    /// <summary>
    /// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class StormReplay
    {
        public int? Id
        {
            get
            {
                try
                {
                    return int.Parse(System.IO.Path.GetFileName(Path).Split(Constants.STORM_REPLAY_CACHED_FILE_NAME_SPLITTER)[0]);
                }
                catch
                {
                    return null;
                }
            }
        }

        public string Path { get; }

        public Replay Replay { get; }

        public StormReplay(string path, Replay replay)
        {
            Replay = replay;
            Path = path;
        }
    }
}