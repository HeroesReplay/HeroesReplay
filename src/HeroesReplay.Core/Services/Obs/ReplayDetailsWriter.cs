using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Runner;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ReplayDetailsWriter : IReplayDetailsWriter
    {
        private readonly ILogger<ReplayDetailsWriter> logger;
        private readonly ISessionHolder sessionHolder;
        private readonly IGameData gameData;
        private readonly AppSettings settings;

        public ReplayDetailsWriter(ILogger<ReplayDetailsWriter> logger, ISessionHolder sessionHolder, AppSettings settings, IGameData gameData)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task WriteObsDetails()
        {
            if (settings.ReplayDetailsWriter.Enabled)
            {
                try
                {
                    var replay = sessionHolder.Current.StormReplay;

                    var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                               from hero in gameData.Heroes
                               where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                               select $"{hero.Name}";

                    if (bans.Any()) bans = bans.Prepend("Bans:");

                    logger.LogInformation($"writing replay details to: {settings.CurrentReplayInfoFilePath}");

                    var lines = new[]
                    {
                        settings.ReplayDetailsWriter.Requestor ? (replay.Request != null ? $"Requestor: {replay.Request.Login}" : string.Empty) : string.Empty,
                        settings.ReplayDetailsWriter.GameMode ? replay.GameType : string.Empty
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

        public async Task ClearDetailsAsync()
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