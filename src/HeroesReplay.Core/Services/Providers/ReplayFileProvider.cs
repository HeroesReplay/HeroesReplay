using System;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Providers
{

    public sealed class ReplayFileProvider : IReplayProvider
    {
        private readonly ILogger<ReplayFileProvider> logger;
        private readonly IReplayLoader loader;
        private readonly AppSettings settings;
        private readonly IReplayHelper replayHelper;
        private readonly FileInfo fileInfo;

        public ReplayFileProvider(ILogger<ReplayFileProvider> logger, IReplayLoader loader, AppSettings settings, IReplayHelper replayHelper)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
            this.fileInfo = new FileInfo(settings.Location.ReplaySource);
        }

        public async Task<LoadedReplay> TryLoadNextReplayAsync()
        {
            Replay replay = await loader.LoadAsync(fileInfo.FullName);

            if (replay != null)
            {
                if (replayHelper.TryGetReplayId(fileInfo.FullName, out int replayId))
                {
                    return new LoadedReplay
                    {
                        FileInfo = fileInfo,
                        Replay = replay,
                        ReplayId = replayId,
                        RewardQueueItem = null,
                        HeroesProfileReplay = null
                    };
                }
                else
                {
                    return new LoadedReplay
                    {
                        FileInfo = fileInfo,
                        Replay = replay,
                        ReplayId = replayId,
                        HeroesProfileReplay = null,
                        RewardQueueItem = null
                    };
                }
            }

            return null;
        }
    }
}