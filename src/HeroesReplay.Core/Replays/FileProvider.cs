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
    public sealed class FileProvider : IReplayProvider
    {
        private readonly ILogger<DirectoryProvider> logger;
        private readonly ReplayHelper replayHelper;
        private readonly Queue<string> queue;

        public FileProvider(ILogger<DirectoryProvider> logger, IConfiguration configuration, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.replayHelper = replayHelper;
            queue = new Queue<string>(new[] { configuration.GetValue<string>(Constants.ConfigKeys.ReplaySource) });
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            while (queue.TryDequeue(out var path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path), replayHelper.ReplayParseOptions);

                logger.LogDebug("result: {0}, path: {1}", result, path);

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    return new StormReplay(path, replay);
                }
            }

            return null;
        }
    }
}