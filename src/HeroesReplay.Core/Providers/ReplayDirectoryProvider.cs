using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using static Heroes.ReplayParser.DataParser;
using static System.IO.File;

namespace HeroesReplay.Core.Providers
{
    public sealed class ReplayDirectoryProvider : IReplayProvider
    {
        private readonly ILogger<ReplayDirectoryProvider> logger;
        private readonly AppSettings settings;
        private readonly ReplayHelper replayHelper;
        private readonly Queue<string> queue;

        public ReplayDirectoryProvider(ILogger<ReplayDirectoryProvider> logger, AppSettings settings, ReplayHelper replayHelper)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
            queue = new Queue<string>(Directory.GetFiles(settings.Location.ReplaySourcePath, settings.StormReplay.WildCard, SearchOption.AllDirectories).OrderBy(GetCreationTime));
        }

        public async Task<StormReplay> TryLoadReplayAsync()
        {
            var parseOptions = new ParseOptions
            {
                AllowPTR = false,
                ShouldParseDetailedBattleLobby = settings.ParseOptions.ShouldParseEvents,
                ShouldParseEvents = settings.ParseOptions.ShouldParseEvents,
                ShouldParseMouseEvents = settings.ParseOptions.ShouldParseMouseEvents,
                ShouldParseStatistics = settings.ParseOptions.ShouldParseStatistics,
                ShouldParseUnits = settings.ParseOptions.ShouldParseUnits,
                ShouldParseMessageEvents = settings.ParseOptions.ShouldParseMessageEvents,
                IgnoreErrors = false
            };

            if (queue.TryDequeue(out string path))
            {
                (ReplayParseResult result, Replay replay) = ParseReplay(await ReadAllBytesAsync(path).ConfigureAwait(false), parseOptions);

                logger.LogInformation($"{path}:{result}");

                if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
                {
                    if (replayHelper.TryGetReplayId(path, out int replayId))
                    {
                        logger.LogInformation($"Replay id found for file: {replayId}");
                    }

                    if (replayHelper.TryGetGameType(path, out string gameType))
                    {
                        logger.LogInformation($"Replay id {replayId} found with GameType: {gameType}");
                    }

                    return new StormReplay(path, replay, replayId, gameType);
                }
            }

            return null;
        }
    }
}
