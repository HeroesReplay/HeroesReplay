using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ReplayDetailsWriter
    {
        private readonly ILogger<ReplayDetailsWriter> logger;
        private readonly IGameData gameData;
        private readonly AppSettings settings;

        public ReplayDetailsWriter(ILogger<ReplayDetailsWriter> logger, AppSettings settings, IGameData gameData)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            if(replay == null) 
                throw new ArgumentNullException(nameof(replay));

            try
            {
                var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                           from hero in gameData.Heroes
                           where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                           select $"{hero.Name}";

                if (bans.Any()) bans = bans.Prepend("Bans:");

                logger.LogInformation($"writing replay details to: {settings.CurrentReplayInfoFilePath}");

                var lines = new[] { settings.ReplayDetailsWriter.GameMode ? replay.GameType : string.Empty }.Concat(settings.ReplayDetailsWriter.Bans ? bans : Enumerable.Empty<string>())
                    .Where(line => !string.IsNullOrWhiteSpace(line));

                await File.WriteAllLinesAsync(settings.CurrentReplayInfoFilePath, lines, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update the file: {settings.CurrentReplayInfoFilePath}");
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
    }
}