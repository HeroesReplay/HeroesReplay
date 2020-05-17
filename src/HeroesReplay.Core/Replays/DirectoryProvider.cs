using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static System.IO.File;

namespace HeroesReplay.Core.Replays
{
    public sealed class DirectoryProvider : IReplayProvider
    {
        private readonly ILogger<DirectoryProvider> logger;
        private readonly ReplayHelper replayHelper;
        private readonly Queue<string> queue;

        public DirectoryProvider(ILogger<DirectoryProvider> logger, IOptions<Settings> settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.replayHelper = replayHelper;
            queue = new Queue<string>(Directory.GetFiles(settings.Value.ReplaySource, Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).OrderBy(GetCreationTime));
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out string? path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await ReadAllBytesAsync(path), replayHelper.ReplayParseOptions);

                logger.LogInformation($"{path}:{result}");

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    return new StormReplay(path, replay);
                }
            }

            return null;
        }
    }
}
