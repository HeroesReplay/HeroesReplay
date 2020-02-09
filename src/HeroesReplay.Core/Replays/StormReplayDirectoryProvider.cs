using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static System.IO.File;

namespace HeroesReplay.Core.Replays
{
    public sealed class StormReplayDirectoryProvider : IStormReplayProvider
    {
        private readonly ILogger<StormReplayDirectoryProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayDirectoryProvider(IConfiguration configuration, ILogger<StormReplayDirectoryProvider> logger)
        {
            this.logger = logger;
            queue = new Queue<string>(Directory.GetFiles(configuration.GetValue<string>(Constants.ConfigKeys.ReplayProviderPath), Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).OrderBy(GetCreationTime));
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out string? path))
            {
                logger.LogInformation("file dequeued: " + path);

                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await ReadAllBytesAsync(path), Constants.REPLAY_PARSE_OPTIONS);

                logger.LogInformation("result: {0}, path: {1}", result, path);

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    return new StormReplay(path, replay);
                }
            }

            return null;
        }
    }
}
