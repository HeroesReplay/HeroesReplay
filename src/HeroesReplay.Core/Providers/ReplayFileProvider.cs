using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Providers
{
    public sealed class ReplayFileProvider : IReplayProvider
    {
        private readonly ILogger<ReplayFileProvider> logger;
        private readonly AppSettings settings;
        private readonly IReplayHelper replayHelper;
        private readonly Queue<string> queue;

        public ReplayFileProvider(ILogger<ReplayFileProvider> logger, AppSettings settings, IReplayHelper replayHelper)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
            queue = new Queue<string>(new[] { settings.Location.ReplaySource });
        }

        public async Task<StormReplay> TryLoadReplayAsync()
        {
            var options = new ParseOptions
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

            while (queue.TryDequeue(out var path))
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path).ConfigureAwait(false), options);

                logger.LogDebug("result: {0}, path: {1}", result, path);

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    if (replayHelper.TryGetReplayId(path, out int replayId))
                    {
                        logger.LogInformation($"Replay id found for {path}: {replayId}");
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