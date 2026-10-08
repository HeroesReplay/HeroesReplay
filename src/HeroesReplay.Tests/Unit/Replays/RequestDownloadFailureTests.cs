using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>
/// #351: the download role took a reward request off <c>Data\requests.json</c> before its replay
/// was downloaded, so a failed download dropped the viewer's redemption: no replay, no refund,
/// nothing on the queue page. These run the download role's <see cref="HeroesProfileProvider"/>
/// against the real <see cref="RequestQueue"/> files in a temp folder, with a fake Heroes
/// Profile. Nothing calls Twitch or Heroes Profile.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RequestDownloadFailureTests : IDisposable
{
    private const int Requested = 65625279;
    private const int Second = 65625300;
    private const string Floor = "2.57.0.98285";
    private const string Current = "2.57.0.98348";
    private static readonly Guid Redemption = Guid.Parse("0a018521-19d9-437d-904b-4096c971d461");
    private static readonly Guid Reward = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-request-download-" + Guid.NewGuid().ToString("N")
    );
    private readonly string mutexName =
        @"Local\HeroesReplay.RequestQueue.Test." + Guid.NewGuid().ToString("N");
    private readonly List<RequestQueue> queues = new();
    private readonly RequestDownloads service = new();
    private readonly AppSettings settings;
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public RequestDownloadFailureTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
                MinReplayId = 65580000,
                GameTypes = new[] { "Storm League" },
                StandardMaxReplayAge = TimeSpan.Zero,
                APIRetryWaitTime = TimeSpan.Zero,
            },
            StormReplay = new StormReplaySettings
            {
                Seperator = "_",
                WildCard = "*.StormReplay",
                FileExtension = ".StormReplay",
            },
            Twitch = new TwitchSettings
            {
                EnableRequests = true,
                QueueFileName = "requests.json",
                FailedFileName = "failed-requests.json",
            },
            Retention = new RetentionSettings { Enabled = false },
            Spectate = new SpectateSettings { MinimumGameVersion = Floor },
        };
        service.Add(Requested);
        service.Add(Second);
    }

    public void Dispose()
    {
        foreach (RequestQueue queue in queues)
        {
            queue.Dispose();
        }

        TestTemp.Delete(root);
    }

    [Fact]
    public async Task HappyPath_RequestLeavesTheQueueOnlyOnceItsReplayIsOnDisk()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        int queuedDuringDownload = -1;
        service.Script.Enqueue(
            async (destination, token) =>
            {
                queuedDuringDownload = Queued().Count;
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, token);
            }
        );

        Assert.True(await Provider(queue).DownloadNextAsync());

        Assert.Equal(1, queuedDuringDownload);
        Assert.Empty(Queued());
        string replay = Assert.Single(ReplayFiles());
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(replay));
        Assert.Equal(Redemption, CachedRequestReward.Read(replay)?.Request?.RedemptionId);
        Assert.Empty(Dispositions());
        Assert.Empty(Failed());
    }

    /// <summary>
    /// A 5xx or 429 that is still failing after the HTTP pipeline's retries keeps the request
    /// queued with a backoff, and is never refunded.
    /// </summary>
    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(403)]
    public async Task TransientFailure_StaysQueuedAndWaitsForItsBackoff(int status)
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        HeroesProfileProvider provider = Provider(queue);
        service.Script.Enqueue(
            (_, _) =>
                throw new ApiException("Heroes Profile refused") { ResponseStatusCode = status }
        );

        Assert.False(await provider.DownloadNextAsync());

        RewardQueueItem queued = Assert.Single(Queued());
        Assert.Equal(1, queued.Download.Attempts);
        Assert.Equal("HTTP " + status, queued.Download.LastError);
        Assert.Equal(now + RequestDownloadRetry.FirstDelay, queued.Download.NextAttemptAt);
        Assert.Null(queued.Download.FailedAt);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        Assert.Empty(Dispositions());
        Assert.Empty(Failed());
        Assert.Contains("download retry 1", Board());

        // Inside the backoff the request is not downloaded again.
        now = now.AddSeconds(30);
        Assert.False(await provider.DownloadNextAsync());
        Assert.Single(service.Downloaded);
        Assert.Single(Queued());

        now = now.Add(RequestDownloadRetry.FirstDelay);
        Assert.True(await provider.DownloadNextAsync());

        Assert.Equal(new[] { Requested, Requested }, service.Downloaded);
        Assert.Empty(Queued());
        Assert.Single(ReplayFiles());
        Assert.Empty(Dispositions());
    }

    /// <summary>
    /// No answer from Heroes Profile still counts toward the role's outage mode, and the request
    /// stays queued for its next attempt.
    /// </summary>
    [Fact]
    public async Task NetworkFailure_StillThrowsAndKeepsTheRequestQueued()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        service.Script.Enqueue((_, _) => throw new HttpRequestException("No such host is known."));

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(queue).DownloadNextAsync());

        RewardQueueItem queued = Assert.Single(Queued());
        Assert.Equal(1, queued.Download.Attempts);
        Assert.Contains(
            "HttpRequestException",
            queued.Download.LastError,
            StringComparison.Ordinal
        );
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        Assert.Empty(Dispositions());
    }

    [Fact]
    public async Task RequestInBackoff_DoesNotHoldBackTheNextRequest()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        Guid second = Guid.NewGuid();
        Assert.True((await queue.EnqueueItemAsync(Request(Second, second))).Success);
        HeroesProfileProvider provider = Provider(queue);
        service.Script.Enqueue(
            (_, _) => throw new ApiException("Bad Gateway") { ResponseStatusCode = 502 }
        );

        Assert.False(await provider.DownloadNextAsync());
        Assert.True(await provider.DownloadNextAsync());

        Assert.Equal(new[] { Requested, Second }, service.Downloaded);
        RewardQueueItem waiting = Assert.Single(Queued());
        Assert.Equal(Redemption, waiting.Request.RedemptionId);
        Assert.StartsWith(Second + "_", Path.GetFileName(Assert.Single(ReplayFiles())));
    }

    /// <summary>
    /// Heroes Profile answering 404 or 410 means the file is gone. The request is failed, a
    /// cancel is recorded for twitch connect, and the queue page says why.
    /// </summary>
    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task PermanentFailure_FailsTheRequestAndRecordsACancel(int status)
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        HeroesProfileProvider provider = Provider(queue);
        service.Script.Enqueue(
            (_, _) => throw new ApiException("Not Found") { ResponseStatusCode = status }
        );

        Assert.False(await provider.DownloadNextAsync());

        Assert.Empty(Queued());
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        RewardQueueItem failed = Assert.Single(Failed());
        Assert.Equal(Redemption, failed.Request.RedemptionId);
        Assert.Equal(now, failed.Download.FailedAt);
        Assert.True(failed.Download.RefundRequested);
        Assert.Contains("HTTP " + status, failed.Download.FailureReason, StringComparison.Ordinal);
        RedemptionDispositionLine cancel = Assert.Single(Dispositions());
        Assert.Equal(
            new RedemptionDispositionLine(
                Requested,
                RedemptionEnd.Cancel,
                Redemption,
                Reward,
                "123456"
            ),
            cancel
        );
        string board = Board();
        Assert.Contains("Could not play", board);
        Assert.Contains("zemill", board);
        Assert.Contains("replay " + Requested, board);
        Assert.Contains("refund requested", board);

        // Nothing is left to try, and the cancel is recorded once.
        Assert.False(await provider.DownloadNextAsync());
        Assert.Single(service.Downloaded);
        Assert.Single(Dispositions());
    }

    /// <summary>
    /// #361: Heroes Profile answers a replay it deleted with 403 and <c>replay_deleted</c>. The
    /// body travels from the Heroes Profile client (over a fake HTTP handler, no network) to the
    /// classifier: the request fails for good and a cancel returns the points.
    /// </summary>
    [Fact]
    public async Task ReplayDeleted_FailsTheRequestAndRecordsACancel()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        HeroesProfileProvider provider = Provider(queue);
        service.Script.Enqueue(
            (destination, token) =>
                HeroesProfileOver(Forbidden(ReplayDeletedBody))
                    .DownloadReplayAsync(Requested, destination, token)
        );

        Assert.False(await provider.DownloadNextAsync());

        Assert.Empty(Queued());
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        RewardQueueItem failed = Assert.Single(Failed());
        Assert.Equal(Redemption, failed.Request.RedemptionId);
        Assert.True(failed.Download.RefundRequested);
        Assert.Equal(
            "Heroes Profile no longer has the replay file (HTTP 403 replay_deleted)",
            failed.Download.FailureReason
        );
        RedemptionDispositionLine cancel = Assert.Single(Dispositions());
        Assert.Equal(RedemptionEnd.Cancel, cancel.End);
        Assert.Equal(Redemption, cancel.RedemptionId);
        string board = Board();
        Assert.Contains("Could not play", board);
        Assert.Contains("refund requested", board);

        // Nothing is left to try, and the cancel is recorded once.
        Assert.False(await provider.DownloadNextAsync());
        Assert.Single(service.Downloaded);
        Assert.Single(Dispositions());
    }

    /// <summary>
    /// #361: any other 403 can be a key or plan problem, which can be fixed. The request stays
    /// queued with its backoff, and its points are not returned.
    /// </summary>
    [Theory]
    [InlineData(
        """{"error":{"code":"endpoint_not_in_plan","message":"Not in your plan.","endpoint":"replay_download"}}""",
        "HTTP 403 endpoint_not_in_plan"
    )]
    [InlineData("""{"message":"Forbidden"}""", "HTTP 403")]
    [InlineData("<html><body>403 Forbidden</body></html>", "HTTP 403")]
    [InlineData("", "HTTP 403")]
    public async Task OtherForbidden_StaysQueuedAndIsNotRefunded(string body, string lastError)
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        HeroesProfileProvider provider = Provider(queue);
        service.Script.Enqueue(
            (destination, token) =>
                HeroesProfileOver(Forbidden(body))
                    .DownloadReplayAsync(Requested, destination, token)
        );

        Assert.False(await provider.DownloadNextAsync());

        RewardQueueItem queued = Assert.Single(Queued());
        Assert.Equal(1, queued.Download.Attempts);
        Assert.Equal(lastError, queued.Download.LastError);
        Assert.Equal(now + RequestDownloadRetry.FirstDelay, queued.Download.NextAttemptAt);
        Assert.Null(queued.Download.FailedAt);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        Assert.Empty(Dispositions());
        Assert.Empty(Failed());
    }

    private const string ReplayDeletedBody =
        """{"error":{"code":"replay_deleted","message":"That replay is no longer stored.","endpoint":"replay_download"}}""";

    private static HttpResponseMessage Forbidden(string body) =>
        new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>The Heroes Profile client over a handler that gives every call this answer.</summary>
    private static HeroesProfileClient HeroesProfileOver(HttpResponseMessage answer) =>
        HeroesProfileClientFactory.Create(
            "test-key",
            new HttpClient(new OneAnswer(answer)),
            new Uri("https://www.heroesprofile.com/api/external/v1/")
        );

    private sealed class OneAnswer(HttpResponseMessage answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(answer);
    }

    [Fact]
    public async Task ReplayBelowTheSupportedPatchLine_FailsWithoutADownload()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        // The patch line moved on while the request waited.
        settings.Spectate.MinimumGameVersion = "2.58.0.10000";

        Assert.False(await Provider(queue).DownloadNextAsync());

        Assert.Empty(service.Downloaded);
        Assert.Empty(Queued());
        RewardQueueItem failed = Assert.Single(Failed());
        Assert.Contains(
            "older than the supported patch line",
            failed.Download.FailureReason,
            StringComparison.Ordinal
        );
        Assert.Equal(RedemptionEnd.Cancel, Assert.Single(Dispositions()).End);
    }

    /// <summary>A stop mid-download is not an attempt: the request stays as it was.</summary>
    [Fact]
    public async Task StopMidDownload_LeavesTheRequestQueuedAndTheNextRunDownloadsIt()
    {
        RequestQueue first = await QueueWith(Requested, Redemption);
        using var stop = new CancellationTokenSource();
        service.Script.Enqueue(
            async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 9 }, CancellationToken.None);
                stop.Cancel();
                token.ThrowIfCancellationRequested();
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(first, stop.Token).DownloadNextAsync()
        );

        RewardQueueItem queued = Assert.Single(Queued());
        Assert.Null(queued.Download);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));

        // The role restarts.
        Assert.True(await Provider(Queue()).DownloadNextAsync());

        Assert.Equal(new[] { Requested, Requested }, service.Downloaded);
        Assert.Empty(Queued());
        Assert.Single(ReplayFiles());
        Assert.Single(Sidecars());
        Assert.Empty(Dispositions());
    }

    /// <summary>
    /// Killed after the replay landed but before the request left the queue: the next run finds
    /// the replay by id, even under another name, and only dequeues. One file, one request.
    /// </summary>
    [Fact]
    public async Task KilledBetweenDownloadAndDequeue_NextRunDequeuesWithoutASecondDownload()
    {
        RequestQueue first = await QueueWith(Requested, Redemption);
        // The rank lookup fills the file name, and may not answer the same on the next run.
        service.Ranks.Enqueue("Gold");
        service.Ranks.Enqueue(null);

        await Assert.ThrowsAsync<ProcessKilled>(() =>
            Provider(new KilledAfterPublish(first)).DownloadNextAsync()
        );

        Assert.Single(Queued());
        string landed = Assert.Single(ReplayFiles());
        Assert.Contains("_Gold_", Path.GetFileName(landed), StringComparison.Ordinal);

        Assert.True(await Provider(Queue()).DownloadNextAsync());

        Assert.Single(service.Downloaded);
        Assert.Empty(Queued());
        Assert.Equal(landed, Assert.Single(ReplayFiles()));
        Assert.Equal(Redemption, CachedRequestReward.Read(landed)?.Request?.RedemptionId);
        Assert.Single(Sidecars());
        Assert.Empty(Dispositions());
        Assert.Empty(Failed());
    }

    /// <summary>Its match was already played and verified: no second download or play.</summary>
    [Fact]
    public async Task RequestAlreadyFulfilled_LeavesTheQueueWithoutADownload()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        RedemptionDispositionLog.Append(
            DispositionsPath(),
            Requested,
            Request(Requested, Redemption),
            RedemptionEnd.Fulfill
        );

        Assert.False(await Provider(queue).DownloadNextAsync());

        Assert.Empty(service.Downloaded);
        Assert.Empty(Queued());
        Assert.Empty(ReplayFiles());
        Assert.Equal(RedemptionEnd.Fulfill, Assert.Single(Dispositions()).End);
    }

    /// <summary>A run that recorded the cancel was killed before the request left the queue.</summary>
    [Fact]
    public async Task CancelAlreadyRecorded_FinishesTheFailureWithoutASecondCancel()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        RedemptionDispositionLog.Append(
            DispositionsPath(),
            Requested,
            Request(Requested, Redemption),
            RedemptionEnd.Cancel
        );

        Assert.False(await Provider(queue).DownloadNextAsync());

        Assert.Empty(service.Downloaded);
        Assert.Empty(Queued());
        Assert.True(Assert.Single(Failed()).Download.RefundRequested);
        Assert.Single(Dispositions());
    }

    [Fact]
    public async Task ViewerRemovesTheRequestDuringItsDownload_NothingIsPublished()
    {
        RequestQueue queue = await QueueWith(Requested, Redemption);
        service.Script.Enqueue(
            async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 1 }, token);
                Assert.NotNull(await queue.RemoveItemAsync("zemill"));
            }
        );

        Assert.False(await Provider(queue).DownloadNextAsync());

        Assert.Empty(Queued());
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        Assert.Empty(Dispositions());
    }

    private async Task<RequestQueue> QueueWith(int replayId, Guid redemption)
    {
        RequestQueue queue = Queue();
        RewardResponse response = await queue.EnqueueItemAsync(Request(replayId, redemption));
        Assert.True(response.Success, response.Message);
        return queue;
    }

    private RequestQueue Queue()
    {
        var queue = new RequestQueue(
            NullLogger<RequestQueue>.Instance,
            service,
            settings,
            TimeSpan.FromSeconds(5),
            mutexName,
            mutexName + ".failed"
        );
        queues.Add(queue);
        return queue;
    }

    private HeroesProfileProvider Provider(IRequestQueue queue, CancellationToken token = default)
    {
        var provider = new HeroesProfileProvider(
            NullLogger<HeroesProfileProvider>.Instance,
            new NoLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            queue,
            service,
            new CancellationTokenProvider(token),
            settings
        );
        provider.UseInstalledVersions(() => new[] { Current });
        provider.UseClock(() => now);
        return provider;
    }

    private static RewardRequest Request(int replayId, Guid redemption) =>
        new()
        {
            Login = "zemill",
            RedemptionId = redemption,
            RewardId = Reward,
            BroadcasterId = "123456",
            RewardTitle = "ReplayId",
            ReplayId = replayId,
        };

    private IReadOnlyList<RewardQueueItem> Queued() =>
        RequestQueue.Snapshot(Path.Combine(root, "requests.json"));

    private IReadOnlyList<RewardQueueItem> Failed() =>
        RequestQueue.Snapshot(Path.Combine(root, "failed-requests.json"));

    private IReadOnlyList<RedemptionDispositionLine> Dispositions() =>
        RedemptionDispositionLog.Read(DispositionsPath());

    private string DispositionsPath() => Path.Combine(root, RedemptionDispositionLog.FileName);

    private string Board() => File.ReadAllText(Path.Combine(root, QueueBoard.FileName));

    private string[] ReplayFiles() =>
        Directory.GetFiles(Path.Combine(root, "Requests"), "*.StormReplay");

    private string[] Sidecars() =>
        Directory.GetFiles(Path.Combine(root, "Requests"), "*" + CachedRequestReward.Extension);

    private sealed class ProcessKilled : Exception { }

    /// <summary>The process dies after the replay is renamed into place, before the queue is saved.</summary>
    private sealed class KilledAfterPublish : IRequestQueue
    {
        private readonly IRequestQueue inner;

        public KilledAfterPublish(IRequestQueue inner)
        {
            this.inner = inner;
        }

        public Task<RequestCompletion> CompleteDownloadAsync(
            RewardQueueItem item,
            Action publish
        ) =>
            inner.CompleteDownloadAsync(
                item,
                () =>
                {
                    publish?.Invoke();
                    throw new ProcessKilled();
                }
            );

        public Task<RewardQueueItem> PeekDownloadAsync(DateTimeOffset now) =>
            inner.PeekDownloadAsync(now);

        public Task<RequestDownload> RetryDownloadLaterAsync(
            RewardQueueItem item,
            string error,
            DateTimeOffset now
        ) => inner.RetryDownloadLaterAsync(item, error, now);

        public Task<bool> FailDownloadAsync(
            RewardQueueItem item,
            string reason,
            bool refundRequested,
            DateTimeOffset now
        ) => inner.FailDownloadAsync(item, reason, refundRequested, now);

        public Task<RewardResponse> EnqueueItemAsync(RewardRequest request) =>
            inner.EnqueueItemAsync(request);

        public Task<int> GetItemsInQueue() => inner.GetItemsInQueue();

        public Task<RewardQueueItem> FindByIndexAsync(int index) => inner.FindByIndexAsync(index);

        public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login) =>
            inner.RemoveItemAsync(login);

        public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login) =>
            inner.FindNextByLoginAsync(login);
    }

    private sealed class NoLoader : IReplayLoader
    {
        public Task<Heroes.ReplayParser.Replay> LoadAsync(string path) =>
            throw new NotSupportedException();
    }

    private sealed class RequestDownloads : IHeroesProfileService
    {
        private readonly Dictionary<int, string> versions = new();

        /// <summary>The next downloads, in order. A download with none left writes one byte.</summary>
        public Queue<Func<Stream, CancellationToken, Task>> Script { get; } = new();

        /// <summary>The rank each lookup fills in, in order. None left keeps the rank.</summary>
        public Queue<string> Ranks { get; } = new();

        public List<int> Downloaded { get; } = new();

        public void Add(int replayId, string version = Current) => versions[replayId] = version;

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult(
                versions.TryGetValue(replayId, out string version)
                    ? new HeroesProfileReplay
                    {
                        Id = replayId,
                        GameType = "Storm League",
                        GameVersion = version,
                        Map = "Cursed Hollow",
                        Fingerprint = "abc",
                    }
                    : null
            );

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloaded.Add(replayId);
            if (Script.TryDequeue(out Func<Stream, CancellationToken, Task> step))
            {
                await step(destination, cancellationToken);
                return;
            }

            await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
        }

        public Task EnrichRankAsync(HeroesProfileReplay replay, CancellationToken cancellationToken)
        {
            if (Ranks.TryDequeue(out string rank))
            {
                replay.Rank = rank;
            }

            return Task.CompletedTask;
        }

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(ReplayListing.Empty);

        public Task<int> GetMaxReplayIdAsync() => Task.FromResult(0);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => Task.FromResult(Enumerable.Empty<HeroesProfileReplay>());

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            Task.FromResult(Enumerable.Empty<HeroesProfileReplay>());

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());
    }
}
