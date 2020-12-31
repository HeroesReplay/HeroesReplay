using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ReplayFileWriter
    {
        private readonly ILogger<ReplayFileWriter> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly IGameData gameData;
        private readonly Settings settings;

        public ReplayFileWriter(ILogger<ReplayFileWriter> logger, Settings settings, IHeroesProfileService heroesProfileService, IGameData gameData)
        {
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.gameData = gameData;
            this.settings = settings;
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            try
            {
                var mmr = settings.HeroesProfileApi.EnableMMR ? $"Tier: " + await heroesProfileService.GetMMRTier(replay) : string.Empty;

                var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                           from hero in gameData.Heroes
                           where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                           select $"{hero.Name}";

                if (bans.Any()) bans = bans.Prepend("Bans:");

                string[] details = new[] { mmr }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

                logger.LogInformation($"writing replay details to: {settings.CurrentReplayInfoFilePath}");

                await File.WriteAllLinesAsync(settings.CurrentReplayInfoFilePath, details.Concat(bans).Where(line => !string.IsNullOrWhiteSpace(line)), CancellationToken.None);
            }
            catch(Exception e)
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
            catch(Exception e)
            {
                logger.LogError(e, $"Could not clear the file: {settings.CurrentReplayInfoFilePath}");
            }
        }
    }
}