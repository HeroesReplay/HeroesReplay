using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Twitch;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>
/// #165 and #166: on 2026-10-02 replay 65625279 was redeemed with "ReplayId", downloaded into
/// Data\Requests by the downloader, and played by the cache provider with no request attached.
/// The downloader dequeued the request and never wrote it beside the file, so the session was
/// ordinary: no request priority on YouTube, a prediction, and no fulfilment.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RequestedReplayLinkTests : IDisposable
{
    private const int Requested = 65625279;
    private const int Ordinary = 65625001;
    private const string Version = "2.57.0.98304";
    private static readonly Guid Redemption = Guid.Parse("0a018521-19d9-437d-904b-4096c971d461");
    private static readonly Guid Reward = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-request-link-" + Guid.NewGuid().ToString("N")
    );

    public RequestedReplayLinkTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(Path.Combine(root, SpectateQueue.SpectatedFileName), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadedRequest_PlaysFromTheCacheWithItsRedemption()
    {
        AppSettings settings = Settings();
        bool linkedBeforeTheFile = false;
        var downloads = new Downloads(() =>
        {
            linkedBeforeTheFile = Directory
                .GetFiles(Path.Combine(root, "Requests"), "*" + CachedRequestReward.Extension)
                .Any();
        });
        var downloader = new HeroesProfileProvider(
            NullLogger<HeroesProfileProvider>.Instance,
            new VersionLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new QueuedRequest(Item()),
            downloads,
            new CancellationTokenProvider(),
            settings
        );

        Assert.True(await downloader.DownloadNextAsync());
        LoadedReplay loaded = await Cache(settings).TryLoadNextReplayAsync();

        Assert.True(linkedBeforeTheFile);
        Assert.Equal(Requested, loaded.ReplayId);
        Assert.Equal(Redemption, loaded.RewardQueueItem?.Request?.RedemptionId);
        Assert.Equal("zemill", loaded.RewardQueueItem.Request.Login);
        Assert.True(ReplayRequestKind.ViewerEnteredReplayId(loaded));
    }

    [Fact]
    public async Task DownloadThatFails_LeavesNoRequestBehind()
    {
        AppSettings settings = Settings();
        var downloads = new Downloads(() => throw new IOException("connection reset"));
        var downloader = new HeroesProfileProvider(
            NullLogger<HeroesProfileProvider>.Instance,
            new VersionLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new QueuedRequest(Item()),
            downloads,
            new CancellationTokenProvider(),
            settings
        );

        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadNextAsync());

        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
    }

    [Fact]
    public async Task RequestedIdAlsoInTheStandardQueue_LoadsWithItsRequestOnce()
    {
        AppSettings settings = Settings();
        string standardCopy = ReplayFile("Standard", Requested, "Unknown");
        string requestCopy = ReplayFile("Requests", Requested, "Gold");
        CachedRequestReward.Write(requestCopy, Item());
        ReplayFile("Standard", Ordinary, "Gold");
        ReplayCacheProvider cache = Cache(settings);

        LoadedReplay first = await cache.TryLoadNextReplayAsync();
        LoadedReplay second = await cache.TryLoadNextReplayAsync();
        LoadedReplay third = await cache.TryLoadNextReplayAsync();

        Assert.Equal(Requested, first.ReplayId);
        Assert.Equal(Redemption, first.RewardQueueItem?.Request?.RedemptionId);
        Assert.Equal(Ordinary, second.ReplayId);
        Assert.Null(second.RewardQueueItem);
        Assert.Null(third);
        Assert.True(File.Exists(standardCopy));
    }

    [Fact]
    public async Task StandardCopyOfARequestedId_IsLinkedByReplayId()
    {
        AppSettings settings = Settings();
        ReplayFile("Standard", Requested, "Unknown");
        // The request's own file has a different name, for example a rank Heroes Profile
        // filled in later. Only its sidecar is left in Data\Requests.
        CachedRequestReward.Write(
            Path.Combine(
                root,
                "Requests",
                Requested + "_ARAM_Gold_Industrial District_fp.StormReplay"
            ),
            Item()
        );

        LoadedReplay loaded = await Cache(settings).TryLoadNextReplayAsync();

        Assert.Equal(Requested, loaded.ReplayId);
        Assert.Equal(Redemption, loaded.RewardQueueItem?.Request?.RedemptionId);
        Assert.Equal("Industrial District", loaded.HeroesProfileReplay?.Map);
    }

    [Fact]
    public void Attach_LinksAPreloadedOrResumedReplayByIdAndIgnoresALongerId()
    {
        string requests = Path.Combine(root, "Requests");
        CachedRequestReward.Write(
            Path.Combine(requests, Requested + "_ARAM_Gold_Map_fp.StormReplay"),
            Item()
        );
        var preloaded = new LoadedReplay
        {
            ReplayId = Requested,
            FileInfo = new FileInfo(ReplayFile("Standard", Requested, "Unknown")),
        };
        var other = new LoadedReplay { ReplayId = 6562527 };

        Assert.True(CachedRequestReward.Attach(preloaded, requests));
        Assert.False(CachedRequestReward.Attach(preloaded, requests));
        Assert.False(CachedRequestReward.Attach(other, requests));
        Assert.Equal(Redemption, preloaded.RewardQueueItem.Request.RedemptionId);
        Assert.Null(other.RewardQueueItem);
        Assert.Null(CachedRequestReward.FindById(requests, 6562527));
        Assert.Null(CachedRequestReward.FindById(Path.Combine(root, "missing"), Requested));
    }

    [Fact]
    public async Task UnplayedRequest_KeepsItsLinkAndComesBackAheadOfOrdinaryReplays()
    {
        AppSettings settings = Settings();
        string requestCopy = ReplayFile("Requests", Requested, "Gold");
        CachedRequestReward.Write(requestCopy, Item());
        ReplayFile("Standard", 65600001, "Gold");
        ReplayFile("Standard", 65600002, "Gold");
        ReplayFile("Standard", 65600003, "Gold");
        DateTimeOffset now = new(2026, 10, 2, 20, 23, 54, TimeSpan.Zero);
        ReplayCacheProvider cache = Cache(settings);
        cache.UseClock(() => now);

        LoadedReplay first = await cache.TryLoadNextReplayAsync();
        Assert.Equal(Requested, first.ReplayId);

        // #169: LoadTimedOut. Engine defers the replay after one attempt.
        Assert.Equal(
            ReplayRetryAction.Defer,
            ReplayRetryPlan.Decide(MatchOutcome.LoadTimedOut, attempt: 1)
        );
        cache.Defer(first);
        LoadedReplay next = await cache.TryLoadNextReplayAsync();
        Assert.Equal(65600001, next.ReplayId);
        cache.MarkSpectated(next);

        now = now.Add(ReplayRetryPlan.RequestDeferFor).AddSeconds(1);
        LoadedReplay again = await cache.TryLoadNextReplayAsync();

        Assert.Equal(Requested, again.ReplayId);
        Assert.Equal(Redemption, again.RewardQueueItem?.Request?.RedemptionId);
        Assert.True(File.Exists(Path.Combine(root, "leases", Requested + ".txt")));
        Assert.DoesNotContain(
            Requested.ToString(),
            File.ReadAllText(Path.Combine(root, SpectateQueue.SpectatedFileName))
        );
    }

    [Fact]
    public async Task RequestThatKeepsMissing_FallsBackToTheLongDeferral()
    {
        AppSettings settings = Settings();
        CachedRequestReward.Write(ReplayFile("Requests", Requested, "Gold"), Item());
        ReplayFile("Standard", 65600001, "Gold");
        DateTimeOffset now = new(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        ReplayCacheProvider cache = Cache(settings);
        cache.UseClock(() => now);

        for (int miss = 0; miss < ReplayRetryPlan.PromptRequestRetries; miss++)
        {
            LoadedReplay request = await cache.TryLoadNextReplayAsync();
            Assert.Equal(Requested, request.ReplayId);
            cache.Defer(request);
            now = now.Add(ReplayRetryPlan.RequestDeferFor).AddSeconds(1);
        }

        LoadedReplay last = await cache.TryLoadNextReplayAsync();
        Assert.Equal(Requested, last.ReplayId);
        cache.Defer(last);
        now = now.Add(ReplayRetryPlan.RequestDeferFor).AddSeconds(1);

        LoadedReplay ordinary = await cache.TryLoadNextReplayAsync();
        Assert.Equal(65600001, ordinary.ReplayId);
        Assert.Equal(
            ReplayRetryPlan.DeferFor,
            ReplayRetryPlan.DeferWindow(requested: true, ReplayRetryPlan.PromptRequestRetries)
        );
        Assert.Equal(
            ReplayRetryPlan.DeferFor,
            ReplayRetryPlan.DeferWindow(requested: false, earlierDefers: 0)
        );
    }

    private string ReplayFile(string folder, int id, string rank)
    {
        string path = Path.Combine(
            root,
            folder,
            id + "_ARAM_" + rank + "_Industrial District_fp.StormReplay"
        );
        File.WriteAllBytes(path, new byte[] { 1 });
        return path;
    }

    private static RewardQueueItem Item() =>
        new(
            new RewardRequest
            {
                Login = "zemill",
                RedemptionId = Redemption,
                RewardId = Reward,
                BroadcasterId = "123",
                RewardTitle = "ReplayId",
                ReplayId = Requested,
            },
            new HeroesProfileReplay
            {
                Id = Requested,
                GameType = "ARAM",
                Rank = "Gold",
                Map = "Industrial District",
                Fingerprint = "fp",
                GameVersion = Version,
            }
        );

    private AppSettings Settings() =>
        new()
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings
            {
                Seperator = "_",
                WildCard = "*.StormReplay",
                FileExtension = ".StormReplay",
            },
            Twitch = new TwitchSettings { EnableRequests = true },
            Retention = new RetentionSettings { Enabled = false },
            Spectate = new SpectateSettings { MinimumGameVersion = Version },
        };

    private static ReplayCacheProvider Cache(AppSettings settings)
    {
        var cache = new ReplayCacheProvider(
            NullLogger<ReplayCacheProvider>.Instance,
            new VersionLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new Downloads(() => { }),
            new CancellationTokenProvider(),
            settings
        );
        cache.UseInstalledVersions(() => new[] { Version });
        return cache;
    }

    private sealed class VersionLoader : IReplayLoader
    {
        public Task<Replay> LoadAsync(string path) =>
            Task.FromResult(new Replay { ReplayVersion = Version, Map = "Industrial District" });
    }

    private sealed class QueuedRequest : IRequestQueue
    {
        private RewardQueueItem item;

        public QueuedRequest(RewardQueueItem item)
        {
            this.item = item;
        }

        public Task<RewardQueueItem> DequeueItemAsync()
        {
            RewardQueueItem next = item;
            item = null;
            return Task.FromResult(next);
        }

        public Task<RewardResponse> EnqueueItemAsync(RewardRequest request) =>
            throw new NotSupportedException();

        public Task<int> GetItemsInQueue() => Task.FromResult(item == null ? 0 : 1);

        public Task<RewardQueueItem> FindByIndexAsync(int index) =>
            Task.FromResult<RewardQueueItem>(null);

        public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);

        public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);
    }

    private sealed class Downloads : IHeroesProfileService
    {
        private readonly Action beforeWrite;

        public Downloads(Action beforeWrite)
        {
            this.beforeWrite = beforeWrite;
        }

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            beforeWrite();
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
        }

        public Task<int> GetMaxReplayIdAsync() => Task.FromResult(0);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult<HeroesProfileReplay>(null);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(ReplayListing.Empty);

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
