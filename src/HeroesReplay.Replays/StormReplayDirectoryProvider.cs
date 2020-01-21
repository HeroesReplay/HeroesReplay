using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.DataParser;
using static System.IO.File;

namespace HeroesReplay.Replays
{
    public sealed class StormReplayDirectoryProvider : IStormReplayProvider
    {
        private readonly ILogger<StormReplayDirectoryProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayDirectoryProvider(IConfiguration configuration, ILogger<StormReplayDirectoryProvider> logger)
        {
            this.logger = logger;
            this.queue = new Queue<string>(Directory.GetFiles(configuration.GetValue<string>("path"), Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).OrderBy(File.GetCreationTime));
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out string? path))
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
    }
}
