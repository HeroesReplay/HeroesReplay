using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Providers
{
    public class ReplayHelper : IReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;
        private readonly StormReplayOptions stormReplayOptions;

        public ReplayHelper(ILogger<ReplayHelper> logger, IOptions<StormReplayOptions> stormReplayOptions)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.stormReplayOptions = stormReplayOptions.Value;
        }

        public bool TryGetReplayId(string path, out int replayId)
        {
            replayId = -1;

            try
            {
                replayId = int.Parse(Path.GetFileName(path).Split(stormReplayOptions.Seperator)[0]);
                return true;
            }
            catch (Exception)
            {
                logger.LogWarning($"Could not parse the replay ID from {path}.");
            }

            return false;
        }
    }
}