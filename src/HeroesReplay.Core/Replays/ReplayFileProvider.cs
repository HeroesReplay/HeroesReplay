using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Replays
{
    public sealed class ReplayFileProvider : IReplayProvider
    {
        private readonly ILogger<ReplayDirectoryProvider> logger;
        private readonly Settings settings;
        private readonly Queue<string> queue;

        public ReplayFileProvider(ILogger<ReplayDirectoryProvider> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
            this.queue = new Queue<string>(new[] { settings.LocationSettings.ReplaySourcePath });
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {            
            while (queue.TryDequeue(out var path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path), settings.ParseOptions);

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