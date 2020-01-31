namespace HeroesReplay.Replays
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using HeroesReplay.Shared;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Heroes.ReplayParser;


    public sealed class StormReplayFileProvider : IStormReplayProvider
    {
        private readonly ILogger<StormReplayDirectoryProvider> logger;
        private readonly Queue<string> queue;

        public StormReplayFileProvider(ILogger<StormReplayDirectoryProvider> logger, IConfiguration configuration)
        {
            this.logger = logger;
            this.queue = new Queue<string>(new[] { configuration.GetValue<string>("path") });
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            while (queue.TryDequeue(out var path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path), Constants.REPLAY_PARSE_OPTIONS);

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    logger.LogInformation("Parse Success: " + path);
                    return new StormReplay(path, replay);
                }
            }

            return null;
        }
    }
}