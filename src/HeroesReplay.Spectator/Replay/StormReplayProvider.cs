using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.DataParser;
using static System.IO.File;

namespace HeroesReplay.Spectator
{
    public sealed class StormReplayProvider
    {
        public int Count => queue.Count;

        private readonly ILogger<StormReplayProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayProvider(ILogger<StormReplayProvider> logger)
        {
            this.logger = logger;
            queue = new Queue<string>();
        }

        public async Task<StormReplay?> TryLoadReplayAsync(string path)
        {
            (ReplayParseResult result, Replay replay) = ParseReplay(await ReadAllBytesAsync(path), true);

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

                (ReplayParseResult result, Replay replay) = ParseReplay(await ReadAllBytesAsync(path), true);

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

        private async Task MoveAsync(string sourcePath, string destinationPath)
        {
            await using (Stream source = File.Open(sourcePath, FileMode.Open))
            {
                await using (Stream destination = File.Create(Path.Combine(destinationPath)))
                {
                    await source.CopyToAsync(destination);
                }
            }
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
