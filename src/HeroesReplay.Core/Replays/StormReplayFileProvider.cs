using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Replays
{
    public sealed class StormReplayFileProvider : IStormReplayProvider
    {
        private readonly ILogger<StormReplayDirectoryProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayFileProvider(ILogger<StormReplayDirectoryProvider> logger, IConfiguration configuration)
        {
            this.logger = logger;
            queue = new Queue<string>(new[] { configuration.GetValue<string>(Constants.ConfigKeys.ReplayProviderPath) });
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            while (queue.TryDequeue(out var path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path), Constants.REPLAY_PARSE_OPTIONS);

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