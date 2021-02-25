using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Observer;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Stream
{
    public class ReplayDetailsWriter : IReplayDetailsWriter
    {
        private readonly ILogger<ReplayDetailsWriter> logger;
        private readonly IReplayContext context;
        private readonly IGameData gameData;
        private readonly AppSettings settings;

        public ReplayDetailsWriter(ILogger<ReplayDetailsWriter> logger, IReplayContext context, AppSettings settings, IGameData gameData)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task WriteFileForObs()
        {
            if (settings.ReplayDetailsWriter.Enabled)
            {
                try
                {
                    Replay replay = context.Current.LoadedReplay.Replay;
                    string requestor = context.Current?.LoadedReplay?.RewardQueueItem?.Request?.Login;
                    string gameType = context.Current?.LoadedReplay?.HeroesProfileReplay?.GameType;

                    IEnumerable<string> bans = from ban in replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                                               from hero in gameData.Heroes
                                               where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) ||
                                                     ban.Hero.Equals(hero.UnitId, StringComparison.OrdinalIgnoreCase) ||
                                                     ban.Hero.Equals(hero.HyperlinkId, StringComparison.OrdinalIgnoreCase)
                                               select $"{hero.HyperlinkId}";

                    if (bans.Any()) bans = bans.Prepend("Bans:");

                    logger.LogInformation($"writing replay details to: {settings.CurrentReplayInfoFilePath}");

                    var lines = new[]
                    {
                        settings.ReplayDetailsWriter.Requestor ? requestor != null ? $"Requestor: {requestor}" : string.Empty : string.Empty,
                        settings.ReplayDetailsWriter.GameType ? gameType ?? string.Empty: string.Empty,
                    }
                    .Concat(settings.ReplayDetailsWriter.Bans ? bans : Enumerable.Empty<string>())
                    .Where(line => !string.IsNullOrWhiteSpace(line));

                    await File.WriteAllLinesAsync(settings.CurrentReplayInfoFilePath, lines, CancellationToken.None);

                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not update the file: {settings.CurrentReplayInfoFilePath}");
                }
            }
        }

        public async Task ClearFileForObs()
        {
            try
            {
                await File.WriteAllTextAsync(settings.CurrentReplayInfoFilePath, string.Empty, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not clear the file: {settings.CurrentReplayInfoFilePath}");
            }
        }

        public Task WriteYouTubeDetails()
        {
            throw new NotImplementedException();
        }
    }
}