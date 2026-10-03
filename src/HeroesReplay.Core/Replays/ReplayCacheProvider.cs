using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Replays;

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
    private Func<IReadOnlyList<string>> installedVersionSource;
    private readonly Dictionary<int, DateTimeOffset> deferredUntil = new();
    private readonly Dictionary<int, string> knownVersion = new();
    private readonly HashSet<int> announcedMissing = new();
    private readonly HashSet<int> belowFloorIds = new();
    private readonly Dictionary<int, int> requestDefers = new();
    private Func<DateTimeOffset> clock = () => DateTimeOffset.UtcNow;
    private int scanSkipped;
    private bool deferredDirty;
    private LoadedReplay staged;
    private int? heldBackId;
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

    internal void UseInstalledVersions(Func<IReadOnlyList<string>> source)
    {
        installedVersionSource = source;
    }

    internal void UseClock(Func<DateTimeOffset> source)
    {
        clock = source ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<LoadedReplay> TryLoadNextReplayAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay.load");
        activity?.SetTag("replay.source", "cache");
        if (staged != null && !IsHeld(staged.ReplayId))
        {
            LoadedReplay ready = staged;
            staged = null;
            ReleaseExpiredDefers();
            HeroesReplayTelemetry.TagReplay(
                activity,
                ready.FileInfo?.FullName,
                ready.Replay?.Map,
                ready.ReplayId,
                ready.Replay?.ReplayVersion
            );
            return ready;
        }

        Seed();
        ReleaseExpiredDefers();
        IReadOnlyList<string> installed =
            installedVersionSource != null
                ? installedVersionSource()
                : InstalledClientCatalog.FileVersions(settings.Location?.GameInstallDirectory);
        scanSkipped = 0;
        deferredDirty = false;

        List<FileInfo> candidates = Directory
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
                replayHelper.TryGetReplayId(file.Name, out int id)
                && !played.Contains(id)
                && !IsHeld(id)
            )
            .OrderBy(file => IsRequest(file) ? 0 : 1)
            .ThenBy(file =>
            {
                replayHelper.TryGetReplayId(file.Name, out int id);
                return id;
            })
            .ToList();

        List<FileInfo> fresh = new();
        List<FileInfo> older = new();
        foreach (FileInfo candidate in candidates)
        {
            replayHelper.TryGetReplayId(candidate.Name, out int id);
            if (belowFloorIds.Contains(id))
            {
                older.Add(candidate);
            }
            else
            {
                fresh.Add(candidate);
            }
        }

        older.Sort(
            (left, right) =>
            {
                replayHelper.TryGetReplayId(right.Name, out int rightId);
                replayHelper.TryGetReplayId(left.Name, out int leftId);
                return rightId.CompareTo(leftId);
            }
        );

        LoadedReplay chosen = await TakeFirstLaunchableAsync(fresh, installed, activity, true)
            .ConfigureAwait(false);
        if (chosen == null)
        {
            chosen = await TakeFirstLaunchableAsync(older, installed, activity, false)
                .ConfigureAwait(false);
        }

        if (deferredDirty)
        {
            WriteDefers();
        }

        LogSkippedMissing(scanSkipped);
        if (chosen == null)
        {
            activity?.SetTag("replay.empty", true);
        }

        return chosen;
    }

    private async Task<LoadedReplay> TakeFirstLaunchableAsync(
        List<FileInfo> files,
        IReadOnlyList<string> installed,
        Activity activity,
        bool deferWhenMissing
    )
    {
        foreach (FileInfo next in files)
        {
            replayHelper.TryGetReplayId(next.Name, out int replayId);
            if (
                knownVersion.TryGetValue(replayId, out string cachedVersion)
                && !ReplayQueuePick.CanLaunch(cachedVersion, installed)
            )
            {
                if (deferWhenMissing)
                {
                    NoteMissing(replayId);
                }
                else if (announcedMissing.Add(replayId))
                {
                    scanSkipped++;
                }

                continue;
            }

            Replay replay = await loader.LoadAsync(next.FullName).ConfigureAwait(false);
            if (replay == null)
            {
                if (WorkEnvelope.AfterUnreadable() == WorkState.Quarantined)
                {
                    played.Add(replayId);
                    AppendQuarantine(replayId);
                    logger.LogWarning(
                        "Replay {ReplayId} could not be parsed. It is quarantined and is not marked spectated.",
                        replayId
                    );
                }

                continue;
            }

            knownVersion[replayId] = replay.ReplayVersion ?? string.Empty;
            if (!ReplayQueuePick.CanLaunch(replay.ReplayVersion, installed))
            {
                if (deferWhenMissing)
                {
                    NoteMissing(replayId);
                }
                else if (announcedMissing.Add(replayId))
                {
                    scanSkipped++;
                }

                continue;
            }

            string leasePath = ReplayLease.PathFor(settings.Location?.DataDirectory, replayId);
            if (!ReplayLease.Take(leasePath, replayId))
            {
                logger.LogWarning("Replay {ReplayId} was not leased. It stays queued.", replayId);
                continue;
            }

            played.Add(replayId);
            HeroesReplayTelemetry.TagReplay(
                activity,
                next.FullName,
                replay.Map,
                replayId,
                replay.ReplayVersion
            );
            // The request is found by replay id, so a copy in Data\Standard or a file whose
            // sidecar is missing still plays as the viewer's request (#165, #166).
            RewardQueueItem request =
                CachedRequestReward.Read(next.FullName)
                ?? CachedRequestReward.FindById(settings.RequestedReplayCachePath, replayId);
            if (request?.Request != null)
            {
                logger.LogInformation(
                    "Playing requested replay {ReplayId} for {Login} ({Reward}, redemption {RedemptionId}) from {Path}",
                    replayId,
                    request.Request.Login,
                    request.Request.RewardTitle,
                    request.Request.RedemptionId,
                    next.FullName
                );
            }
            else if (IsRequest(next))
            {
                logger.LogWarning(
                    "Playing replay {ReplayId} from {Path}. No request was found for it, so it plays as an ordinary replay.",
                    replayId,
                    next.FullName
                );
            }
            else
            {
                logger.LogInformation(
                    "Playing cached replay {ReplayId} from {Path}",
                    replayId,
                    next.FullName
                );
            }

            HeroesProfileReplay profile = RankFromFile(next.Name, replayId, replay.Map);
            if (profile != null)
            {
                await heroesProfile
                    .EnrichRankAsync(profile, tokenProvider.Token)
                    .ConfigureAwait(false);
            }

            return new LoadedReplay
            {
                FileInfo = next,
                Replay = replay,
                ReplayId = replayId,
                RewardQueueItem = request,
                HeroesProfileReplay = profile ?? request?.HeroesProfileReplay,
            };
        }

        return null;
    }

    private void NoteMissing(int replayId)
    {
        if (!played.Add(replayId))
        {
            return;
        }

        deferredUntil[replayId] = clock().Add(ReplayRetryPlan.DeferFor);
        deferredDirty = true;
        if (announcedMissing.Add(replayId))
        {
            scanSkipped++;
        }
    }

    private void LogSkippedMissing(int newlySkipped)
    {
        if (newlySkipped <= 0)
        {
            return;
        }

        logger.LogInformation(
            "Skipped {Count} replay(s) whose Heroes build is not installed. They stay queued. The waiting scene was not used.",
            newlySkipped
        );
    }

    public void Requeue(LoadedReplay replay)
    {
        if (replay?.ReplayId is not int replayId || replayId <= 0)
        {
            return;
        }

        played.Remove(replayId);
        if (heldBackId == replayId)
        {
            heldBackId = null;
        }

        string path = PlayedPath();
        if (File.Exists(path))
        {
            string[] kept = File.ReadAllLines(path)
                .Where(line => line.Trim() != replayId.ToString())
                .ToArray();
            File.WriteAllLines(path, kept);
        }

        staged = replay;
        logger.LogInformation("Returned replay {ReplayId} to the front of the cache.", replayId);
    }

    public void Defer(LoadedReplay replay)
    {
        if (replay?.ReplayId is not int replayId || replayId <= 0)
        {
            return;
        }

        if (staged?.ReplayId == replayId)
        {
            staged = null;
        }

        bool requested = replay.RewardQueueItem?.Request != null || IsRequest(replay.FileInfo);
        requestDefers.TryGetValue(replayId, out int earlier);
        if (requested)
        {
            requestDefers[replayId] = earlier + 1;
        }

        played.Add(replayId);
        deferredUntil[replayId] = clock().Add(ReplayRetryPlan.DeferWindow(requested, earlier));
        WriteDefers();
        if (requested)
        {
            // The request file and its sidecar stay in Data\Requests, so the link and the lease
            // come back with the replay. The redemption stays UNFULFILLED until it plays.
            logger.LogInformation(
                "Deferred requested replay {ReplayId} until {Until:o} (miss {Miss}). It is not marked spectated, its redemption stays unfulfilled, and it plays again ahead of ordinary replays.",
                replayId,
                deferredUntil[replayId],
                earlier + 1
            );
            return;
        }

        logger.LogInformation(
            "Deferred replay {ReplayId} until {Until:o}. It is not marked spectated.",
            replayId,
            deferredUntil[replayId]
        );
    }

    public void MarkSpectated(LoadedReplay replay)
    {
        if (replay?.ReplayId is not int replayId || replayId <= 0)
        {
            return;
        }

        played.Add(replayId);
        requestDefers.Remove(replayId);
        AppendPlayed(replayId);
        if (staged?.ReplayId == replayId)
        {
            staged = null;
        }

        if (heldBackId == replayId)
        {
            heldBackId = null;
        }
    }

    public void HoldBack(int replayId)
    {
        if (replayId > 0)
        {
            heldBackId = replayId;
        }
    }

    private bool IsHeld(int? replayId)
    {
        return replayId is int id && heldBackId is int held && id == held;
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

            LoadDefers();
            LoadQuarantine();
            LoadBelowFloor();
            seeded = true;
        }
    }

    private void AppendPlayed(int replayId)
    {
        File.AppendAllText(PlayedPath(), replayId + Environment.NewLine);
    }

    private void AppendQuarantine(int replayId)
    {
        Directory.CreateDirectory(settings.Location.DataDirectory);
        File.AppendAllText(QuarantinePath(), replayId + Environment.NewLine);
    }

    private void LoadBelowFloor()
    {
        string path = BelowFloorPath();
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (int.TryParse(line, out int id))
            {
                belowFloorIds.Add(id);
            }
        }
    }

    private void LoadQuarantine()
    {
        string path = QuarantinePath();
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (int.TryParse(line, out int id))
            {
                played.Add(id);
            }
        }
    }

    private void LoadDefers()
    {
        string path = DeferPath();
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            string[] parts = line.Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int id))
            {
                continue;
            }

            if (!long.TryParse(parts[1], out long unix))
            {
                continue;
            }

            deferredUntil[id] = DateTimeOffset.FromUnixTimeSeconds(unix);
            played.Add(id);
        }
    }

    private void WriteDefers()
    {
        Directory.CreateDirectory(settings.Location.DataDirectory);
        string[] lines = deferredUntil
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Key + " " + pair.Value.ToUnixTimeSeconds())
            .ToArray();
        File.WriteAllLines(DeferPath(), lines);
    }

    private void ReleaseExpiredDefers()
    {
        DateTimeOffset now = clock();
        List<int> due = deferredUntil
            .Where(pair => pair.Value <= now)
            .Select(pair => pair.Key)
            .ToList();
        if (due.Count == 0)
        {
            return;
        }

        foreach (int id in due)
        {
            deferredUntil.Remove(id);
            played.Remove(id);
            logger.LogInformation("Replay {ReplayId} is eligible again after its deferral.", id);
        }

        WriteDefers();
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
        string directory = file?.DirectoryName;
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
        Path.Combine(settings.Location.DataDirectory, SpectateQueue.SpectatedFileName);

    private string DeferPath() =>
        Path.Combine(settings.Location.DataDirectory, SpectateQueue.DeferredFileName);

    private string QuarantinePath() =>
        Path.Combine(settings.Location.DataDirectory, SpectateQueue.QuarantineFileName);

    private string BelowFloorPath() =>
        Path.Combine(settings.Location.DataDirectory, SpectateQueue.BelowFloorFileName);
}
