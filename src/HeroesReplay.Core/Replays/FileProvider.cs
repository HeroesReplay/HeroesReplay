using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Replays
{
    public sealed class FileProvider : IReplayProvider
    {
        private readonly ILogger<DirectoryProvider> logger;
        private readonly ReplayHelper replayHelper;
        private readonly Queue<string> queue;
        private readonly Settings settings;

        public FileProvider(ILogger<DirectoryProvider> logger, IOptions<Settings> settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.settings = settings.Value;
            this.replayHelper = replayHelper;
            queue = new Queue<string>(new[] { settings.Value.ReplaySource });
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