using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Providers
{
    public class ReplayLoader : IReplayLoader
    {
        private readonly ILogger<ReplayLoader> logger;
        private readonly AppSettings settings;

        private readonly ParseOptions options;

        public ReplayLoader(ILogger<ReplayLoader> logger, AppSettings settings)
        {
            this.logger = logger;
            this.settings = settings;

            this.options = new()
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
        }

        public async Task<Replay> LoadAsync(string path)
        {
            try
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path).ConfigureAwait(false), options);
                if (result == DataParser.ReplayParseResult.Success || result == DataParser.ReplayParseResult.UnexpectedResult) return replay;
                logger.LogError($"There was an error parsing the replay: {path}. Result: {result}");
            }
            catch (Exception e)
            {
                logger.LogError(e, "There was an error parsing the replay: {path}.");
            }

            return null;
        }
    }
}