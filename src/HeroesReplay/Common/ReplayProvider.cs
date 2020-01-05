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

        public Queue<Game> Games { get; }
        public List<Game> Complete { get; }

        public ReplayProvider(string path)
        {
            this.path = path;

            Games = new Queue<Game>();
            Complete = new List<Game>();
        }

        public void LoadAndParseReplays(int count = 5)
        {
            Console.WriteLine($"Loading and parsing {count} replays into the queue.");

            foreach (var replayPath in Directory.EnumerateFiles(path, "*.StormReplay", SearchOption.AllDirectories).OrderByDescending(x => File.GetCreationTime(x)))
            {
                if (Complete.Any(g => g.Path == replayPath)) continue;

                var (result, replay) = DataParser.ParseReplay(File.ReadAllBytes(replayPath));

                if (result == DataParser.ReplayParseResult.Success && GameSpectator.SupportedModes.Contains(replay.GameMode))
                {
                    Console.WriteLine($"Loaded {replayPath} into the queue.");
                    Games.Enqueue(new Game(replayPath, replay));
                }

                if (Games.Count == count) break;
            }

            return;
        }

        public void Dispose()
        {

        }
    }
}
