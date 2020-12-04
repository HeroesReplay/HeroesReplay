using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Shared
{
    public class ReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;

        public ReplayHelper(ILogger<ReplayHelper> logger)
        {
            this.logger = logger;
        }

        public bool TryGetReplayId(StormReplay stormReplay, out int replayId)
        {
            replayId = -1;

            try
            {
                replayId = int.Parse(Path.GetFileName(stormReplay.Path).Split(Constants.STORM_REPLAY_CACHED_FILE_NAME_SPLITTER)[0]);
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