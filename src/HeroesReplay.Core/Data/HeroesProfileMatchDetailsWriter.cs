using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Runner
{
    public class HeroesProfileMatchDetailsWriter
    {
        private readonly ILogger<HeroesProfileMatchDetailsWriter> logger;
        private readonly HeroesProfileService heroesProfileService;
        private readonly GameDataService gameDataService;
        private readonly Settings settings;

        public HeroesProfileMatchDetailsWriter(ILogger<HeroesProfileMatchDetailsWriter> logger, Settings settings, HeroesProfileService heroesProfileService, GameDataService gameDataService)
        {
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.gameDataService = gameDataService;
            this.settings = settings;
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            var mmr = settings.Toggles.EnableMMR ? $"MMR: " + await heroesProfileService.CalculateMMRAsync(replay) : string.Empty;
            var map = gameDataService.Maps.Find(map => map.AltName.Equals(replay.Replay.MapAlternativeName) || replay.Replay.Map.Equals(map.Name));

            var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                       from hero in gameDataService.Heroes
                       where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                       select $"{hero.Name}";

            if (bans.Any()) bans = bans.Prepend("Bans:");

            string[] details = new[] { mmr, replay.Replay.ReplayVersion }.Where(line => !string.IsNullOrEmpty(line)).ToArray();

            logger.LogInformation($"writing replay details to: {settings.CurrentReplayInfoFilePath}");

            await File.WriteAllLinesAsync(settings.CurrentReplayInfoFilePath, details.Concat(bans).Where(line => !string.IsNullOrWhiteSpace(line)), CancellationToken.None);
        }
    }
}