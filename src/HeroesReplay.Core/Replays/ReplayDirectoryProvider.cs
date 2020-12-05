using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

using static System.IO.File;
using static Heroes.ReplayParser.DataParser;

namespace HeroesReplay.Core.Replays
{
    public sealed class ReplayDirectoryProvider : IReplayProvider
    {
        private readonly ILogger<ReplayDirectoryProvider> logger;
        private readonly Settings settings;
        private readonly Queue<string> queue;

        public ReplayDirectoryProvider(ILogger<ReplayDirectoryProvider> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
            queue = new Queue<string>(Directory.GetFiles(settings.LocationSettings.ReplaySourcePath, settings.StormReplaySettings.StormReplayFileWildCard, SearchOption.AllDirectories).OrderBy(GetCreationTime));
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out string? path))
            {
                (ReplayParseResult result, Replay replay) = ParseReplay(await ReadAllBytesAsync(path), settings.ParseOptions);

                logger.LogInformation($"{path}:{result}");

                if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
                {
                    return new StormReplay(path, replay);
                }
            }

            return null;
        }
    }
}
