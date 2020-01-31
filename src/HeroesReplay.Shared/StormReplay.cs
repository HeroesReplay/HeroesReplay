using System;
using System.IO;

namespace HeroesReplay.Shared
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
                    return int.Parse(System.IO.Path.GetFileName(Path).Split('_')[0]);
                }
                catch
                {
                    return null;
                }
            }
        }

        public string Path { get; }

        public Heroes.ReplayParser.Replay Replay { get; }

        public StormReplay(string path, Heroes.ReplayParser.Replay replay)
        {
            Replay = replay;
            Path = path;
        }
    }
}