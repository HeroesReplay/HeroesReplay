using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Providers
{
    public class ReplayHelper : IReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;
        private readonly AppSettings settings;

        public ReplayHelper(ILogger<ReplayHelper> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool TryGetReplayId(string path, out int replayId)
        {
            replayId = -1;

            try
            {
                replayId = int.Parse(Path.GetFileName(path).Split(settings.StormReplay.Seperator)[0]);
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