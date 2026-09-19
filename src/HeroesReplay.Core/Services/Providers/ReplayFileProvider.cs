using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Providers;

public sealed class ReplayFileProvider : IReplayProvider
{
    private readonly ILogger<ReplayFileProvider> logger;
    private readonly IReplayLoader loader;
    private readonly IReplayHelper replayHelper;
    private readonly Queue<FileInfo> remaining = new();
    private readonly bool playOnce;

    public bool ContinuesWhenEmpty => !playOnce;

    public ReplayFileProvider(
        ILogger<ReplayFileProvider> logger,
        IReplayLoader loader,
        AppSettings settings,
        IReplayHelper replayHelper,
        ReplayPathOptions pathOptions
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        playOnce = pathOptions?.PlayOnce ?? true;
        string path = !string.IsNullOrWhiteSpace(pathOptions?.Path)
            ? pathOptions.Path
            : settings.Location.ReplaySource;
        Seed(path);
    }

    public async Task<LoadedReplay> TryLoadNextReplayAsync()
    {
        while (remaining.Count > 0)
        {
            FileInfo fileInfo = remaining.Dequeue();
            if (!fileInfo.Exists)
            {
                logger.LogWarning("Replay file not found: {Path}", fileInfo.FullName);
                continue;
            }

            Replay replay = await loader.LoadAsync(fileInfo.FullName);
            if (replay == null)
            {
                continue;
            }

            replayHelper.TryGetReplayId(fileInfo.FullName, out int replayId);
            return new LoadedReplay
            {
                FileInfo = fileInfo,
                Replay = replay,
                ReplayId = replayId,
                RewardQueueItem = null,
                HeroesProfileReplay = null,
            };
        }

        return null;
    }

    private void Seed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogError("No replay path was configured.");
            return;
        }

        if (File.Exists(path))
        {
            remaining.Enqueue(new FileInfo(path));
            return;
        }

        if (Directory.Exists(path))
        {
            foreach (
                string file in Directory
                    .EnumerateFiles(path, "*.StormReplay", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            )
            {
                remaining.Enqueue(new FileInfo(file));
            }

            logger.LogInformation("Queued {Count} replay(s) from {Path}", remaining.Count, path);
            return;
        }

        logger.LogError("Replay path does not exist: {Path}", path);
    }
}
