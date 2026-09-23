using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Providers;

/// <summary>
/// Plays .StormReplay files already on disk. The downloader fills Data\Standard and Data\Requests.
/// A filename that only has a league name is looked up with Heroes Profile before the badge is shown.
/// </summary>
public sealed class ReplayCacheProvider : IReplayProvider
{
    private readonly ILogger<ReplayCacheProvider> logger;
    private readonly IReplayLoader loader;
    private readonly IReplayHelper replayHelper;
    private readonly IHeroesProfileService heroesProfile;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly AppSettings settings;
    private readonly HashSet<int> played = new();
    private bool seeded;

    public bool ContinuesWhenEmpty => true;

    public ReplayCacheProvider(
        ILogger<ReplayCacheProvider> logger,
        IReplayLoader loader,
        IReplayHelper replayHelper,
        IHeroesProfileService heroesProfile,
        CancellationTokenProvider tokenProvider,
        AppSettings settings
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
        this.heroesProfile =
            heroesProfile ?? throw new ArgumentNullException(nameof(heroesProfile));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<LoadedReplay> TryLoadNextReplayAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay.load");
        activity?.SetTag("replay.source", "cache");
        Seed();

        FileInfo next = Directory
            .EnumerateFiles(
                settings.StandardReplayCachePath,
                "*.StormReplay",
                SearchOption.TopDirectoryOnly
            )
            .Concat(
                Directory.EnumerateFiles(
                    settings.RequestedReplayCachePath,
                    "*.StormReplay",
                    SearchOption.TopDirectoryOnly
                )
            )
            .Select(path => new FileInfo(path))
            .Where(file =>
                replayHelper.TryGetReplayId(file.Name, out int id) && !played.Contains(id)
            )
            .OrderBy(file => IsRequest(file) ? 0 : 1)
            .ThenBy(file =>
            {
                replayHelper.TryGetReplayId(file.Name, out int id);
                return id;
            })
            .FirstOrDefault();

        if (next == null)
        {
            activity?.SetTag("replay.empty", true);
            return null;
        }

        replayHelper.TryGetReplayId(next.Name, out int replayId);
        Replay replay = await loader.LoadAsync(next.FullName).ConfigureAwait(false);
        if (replay == null)
        {
            played.Add(replayId);
            AppendPlayed(replayId);
            return null;
        }

        played.Add(replayId);
        AppendPlayed(replayId);
        HeroesReplayTelemetry.TagReplay(
            activity,
            next.FullName,
            replay.Map,
            replayId,
            replay.ReplayVersion
        );
        logger.LogInformation(
            IsRequest(next)
                ? "Playing requested replay {ReplayId} from {Path}"
                : "Playing cached replay {ReplayId} from {Path}",
            replayId,
            next.FullName
        );
        HeroesProfileReplay profile = RankFromFile(next.Name, replayId, replay.Map);
        if (profile != null)
        {
            await heroesProfile.EnrichRankAsync(profile, tokenProvider.Token).ConfigureAwait(false);
        }

        return new LoadedReplay
        {
            FileInfo = next,
            Replay = replay,
            ReplayId = replayId,
            RewardQueueItem = null,
            HeroesProfileReplay = profile,
        };
    }

    private void Seed()
    {
        Directory.CreateDirectory(settings.StandardReplayCachePath);
        Directory.CreateDirectory(settings.RequestedReplayCachePath);
        string path = PlayedPath();
        if (!seeded)
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (int.TryParse(line, out int id))
                    {
                        played.Add(id);
                    }
                }
            }
            else
            {
                foreach (
                    string file in Directory.EnumerateFiles(
                        settings.StandardReplayCachePath,
                        "*.StormReplay"
                    )
                )
                {
                    if (replayHelper.TryGetReplayId(Path.GetFileName(file), out int id))
                    {
                        played.Add(id);
                    }
                }

                File.WriteAllLines(path, played.Select(id => id.ToString()));
                logger.LogInformation(
                    "Seeded {Count} existing cache replays as already played.",
                    played.Count
                );
            }

            seeded = true;
        }
    }

    private void AppendPlayed(int replayId)
    {
        File.AppendAllText(PlayedPath(), replayId + Environment.NewLine);
    }

    private static HeroesProfileReplay RankFromFile(string fileName, int replayId, string map)
    {
        string rank = RankImage.RankFromCacheFileName(fileName);
        string gameType = GameTypeFromCacheFileName(fileName);
        if (rank == null && !HeroesProfileRankEnricher.IsStormLeague(gameType))
        {
            return null;
        }

        return new HeroesProfileReplay
        {
            Id = replayId,
            Rank = rank,
            Map = map,
            GameType = gameType,
        };
    }

    private static string GameTypeFromCacheFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string name = Path.GetFileName(fileName);
        int dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name.Substring(0, dot);
        }

        string[] parts = name.Split('_');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private bool IsRequest(FileInfo file)
    {
        string directory = file.DirectoryName;
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(directory),
            Path.GetFullPath(settings.RequestedReplayCachePath),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private string PlayedPath() =>
        Path.Combine(settings.Location.DataDirectory, "spectated-ids.txt");
}
