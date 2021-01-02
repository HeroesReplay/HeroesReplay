using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

using System;
using System.IO;

namespace HeroesReplay.Core.Providers
{
    public class ReplayHelper
    {
        private readonly ILogger<ReplayHelper> logger;
        private readonly AppSettings settings;

        public ReplayHelper(ILogger<ReplayHelper> logger, AppSettings settings)
        {
            this.logger = logger;
            this.settings = settings;
        }

        public bool TryGetReplayId(StormReplay stormReplay, out int replayId)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            return TryGetReplayId(stormReplay.Path, out replayId);
        }

        public bool TryGetGameType(StormReplay stormReplay, out string? gameType)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            return TryGetGameType(stormReplay.Path, out gameType);
        }

        public bool TryGetReplayId(string path, out int replayId)
        {
            replayId = -1;

            try
            {
                replayId = int.Parse(Path.GetFileName(path).Split(settings.StormReplay.Seperator)[0]);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not parse the replay ID from {path}. Is it a S3 replay file with an ID?");
            }

            return false;
        }

        public bool TryGetGameType(string path, out string? gameType)
        {
            gameType = null;

            try
            {
                gameType = Path.GetFileName(path).Split(settings.StormReplay.Seperator)[1];
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not extract GameType from {path}. Is it a S3 replay file with an ID?");
            }

            return false;
        }
    }
}