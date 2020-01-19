using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.DataParser;
using static System.IO.File;

namespace HeroesReplay.Replays
{
    public sealed class StormReplayDirectoryProvider
    {
        public int Count => queue.Count;

        private readonly ILogger<StormReplayDirectoryProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayDirectoryProvider(ILogger<StormReplayDirectoryProvider> logger)
        {
            this.logger = logger;
            queue = new Queue<string>();
        }

        public async Task<StormReplay?> TryLoadReplayAsync(string path)
        {
            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await ReadAllBytesAsync(path), true);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                logger.LogInformation("Parse Success: " + path);
                return new StormReplay(path, replay);
            }

            return null;
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out var path))
            {
                logger.LogInformation("Dequeued: " + path);

                (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await ReadAllBytesAsync(path), true);

                if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
                {
                    logger.LogInformation("Parse Success: " + path);
                    return new StormReplay(path, replay);
                }

                logger.LogInformation("Parse Error: " + path);
                logger.LogInformation("Result: " + result);
            }

            return null;
        }

        public void LoadReplays(string directory)
        {
            foreach (string file in Directory.GetFiles(directory, Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).OrderBy(File.GetCreationTime))
            {
                if (!queue.Contains(file))
                {
                    logger.LogInformation("Queued: " + file);
                    queue.Enqueue(file);
                }
            }
        }
    }
}
