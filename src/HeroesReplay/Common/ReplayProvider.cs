using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeroesReplay
{
    public class ReplayProvider : IDisposable
    {
        private readonly string path;

        public Queue<Game> Unwatched { get; }
        public List<Game> Watched { get; }

        public ReplayProvider(string path)
        {
            this.path = path;

            Unwatched = new Queue<Game>();
            Watched = new List<Game>();
        }

        public void LoadAndParseReplays(int count = 5)
        {
            Console.WriteLine($"Loading and parsing {count} replays into the queue.");

            foreach (var replayPath in Directory.EnumerateFiles(path, "*.StormReplay", SearchOption.AllDirectories).OrderByDescending(x => File.GetCreationTime(x)))
            {
                if (Watched.Any(g => g.Path == replayPath)) continue;

                var (result, replay) = DataParser.ParseReplay(File.ReadAllBytes(replayPath));

                if (result == DataParser.ReplayParseResult.Success && GameSpectator.SupportedModes.Contains(replay.GameMode))
                {
                    Console.WriteLine($"Loaded {replayPath} into the queue.");
                    Unwatched.Enqueue(new Game(replayPath, replay));
                }

                if (Unwatched.Count == count) break;
            }

            return;
        }

        public void Dispose()
        {

        }
    }
}
