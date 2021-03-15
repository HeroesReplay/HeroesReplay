using System;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Providers
{
    public class ReplayLoader : IReplayLoader
    {
        private readonly ILogger<ReplayLoader> logger;
        private readonly ParseOptions parseOptions;

        public ReplayLoader(ILogger<ReplayLoader> logger, IOptions<ParseOptions> parseOptions)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.parseOptions = parseOptions.Value;
        }

        public async Task<Replay> LoadAsync(string path)
        {
            try
            {
                (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(await File.ReadAllBytesAsync(path).ConfigureAwait(false), parseOptions);
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