using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.IO;

namespace HeroesReplay.Core.Providers
{
    public class ReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;
        private readonly Settings settings;

        public ReplayHelper(ILogger<ReplayHelper> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
        }

        public bool TryGetReplayId(StormReplay stormReplay, out int replayId)
        {
            replayId = -1;

            try
            {
                replayId = int.Parse(Path.GetFileName(stormReplay.Path).Split(settings.HotsApi.CachedFileNameSplitter)[0]);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not parse the replay ID from {stormReplay.Path}. Is it a Hots API replay file?");
            }

            return false;
        }
    }
}