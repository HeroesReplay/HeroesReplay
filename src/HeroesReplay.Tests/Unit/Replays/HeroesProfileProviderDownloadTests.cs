using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileProviderDownloadTests
{
    [Fact]
    public async Task DownloadNextAsync_CancelledDownloadDoesNotRetryOrLeaveTheReplay()
    {
        string root = NewRoot();
        using var stop = new CancellationTokenSource();
        var resume = new HeroesProfileResume();
        resume.Arm();
        var service = new ScriptedDownloads(
            async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 });
                stop.Cancel();
                token.ThrowIfCancellationRequested();
            }
        );

        try
        {
            HeroesProfileProvider provider = Provider(root, service, resume, stop.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.DownloadNextAsync()
            );

            Assert.Equal(1, service.Downloads);
            Assert.True(resume.IsPending);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_RetriesOnceAfterAnOutageAndWritesTheWholeReplay()
    {
        string root = NewRoot();
        var resume = new HeroesProfileResume();
        resume.Arm();
        var service = new ScriptedDownloads(
            async (destination, _) =>
            {
                await destination.WriteAsync(new byte[] { 9, 9 });
            }
        );
        service.FailFirst = true;

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume,
                CancellationToken.None
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(2, service.Downloads);
            Assert.False(resume.IsPending);
            string[] files = Directory.GetFiles(Path.Combine(root, "Requests"));
            string replay = Assert.Single(files);
            Assert.EndsWith(".StormReplay", replay);
            Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(replay));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_JumpsPastAStaleCursorToARecentReplay()
    {
        string root = NewRoot();
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId =>
                minId < 65657000
                    ? Replay(minId + 1, now - TimeSpan.FromDays(3))
                    : Replay(minId + 1, now - TimeSpan.FromHours(1))
        );

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65657001, Assert.Single(service.Downloaded));
            Assert.Equal(1, service.MaxIdLookups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>#217: a max age of zero turns catch-up off, with no newest-id lookup.</summary>
    [Fact]
    public async Task DownloadNextAsync_ZeroMaxAgeKeepsTheCursorWithoutAMaxIdLookup()
    {
        string root = NewRoot();
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId => Replay(minId + 1, now - TimeSpan.FromDays(3))
        );

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false,
                maxReplayAge: TimeSpan.Zero
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580001, Assert.Single(service.Downloaded));
            Assert.Equal(0, service.MaxIdLookups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string Previous = "2.57.0.98304";
    private const string Current = "2.57.0.98348";

    [Fact]
    public async Task DownloadNextAsync_TakesTheCurrentPatchOverALowerIdOnAnOlderBuild()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId < 65580002
                    ? Builds((65580001, Previous), (65580002, Current))
                    : ReplayListing.Empty
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580002, Assert.Single(service.Downloaded));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_ReadsAheadForTheCurrentPatch()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65581010,
            listPage: minId =>
                minId switch
                {
                    < 65580001 => Builds((65580001, Previous)),
                    < 65581001 => Builds((65581001, Current)),
                    _ => ReplayListing.Empty,
                }
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65581001, Assert.Single(service.Downloaded));
            Assert.Equal(new[] { 65580000, 65580001 }, service.Listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_FallsBackToThePreviousBuildWhenTheCurrentPatchIsNotListed()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId < 65580002
                    ? Builds((65580001, Previous), (65580002, Previous))
                    : ReplayListing.Empty
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580001, Assert.Single(service.Downloaded));
            Assert.Equal(new[] { 65580000, 65580002 }, service.Listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// #346: a download Heroes Profile answers with an HTTP status (after the pipeline's retries)
    /// is a logged skip, not an outage, and the next call takes the replay after it.
    /// </summary>
    [Fact]
    public async Task DownloadNextAsync_SkipsAReplayWhoseDownloadHeroesProfileRefuses()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        )
        {
            Refuse = id =>
                id == 65580001
                    ? new ApiException("Too Many Requests") { ResponseStatusCode = 429 }
                    : null,
        };
        var logger = new ListLogger();

        try
        {
            HeroesProfileProvider provider = Standard(root, service, logger);

            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Standard")));
            LogLine skip = Assert.Single(
                logger.Lines,
                line => line.Message.StartsWith("Skipped replay", StringComparison.Ordinal)
            );
            Assert.Equal(65580001, skip.Values["ReplayId"]);
            Assert.Equal(429, skip.Values["Status"]);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(new[] { 65580001, 65580002 }, service.Downloaded);
            string file = Assert.Single(Directory.GetFiles(Path.Combine(root, "Standard")));
            Assert.StartsWith("65580002_", Path.GetFileName(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>No answer from Heroes Profile is still a failure for the role's outage count.</summary>
    [Fact]
    public async Task DownloadNextAsync_ANetworkFailureStillThrows()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        )
        {
            Refuse = _ => new HttpRequestException("No such host is known."),
        };

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            await Assert.ThrowsAsync<HttpRequestException>(() => provider.DownloadNextAsync());
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Standard")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const int Limit = 5;

    /// <summary>
    /// #280: 97 week-old replays of a re-downloaded build waited in Data\Standard and stopped
    /// every download. Only replays inside the media window count toward the limit.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(Limit - 1, true)]
    [InlineData(Limit, false)]
    public async Task DownloadNextAsync_OnlyReplaysInsideTheMediaWindowCountTowardTheLimit(
        int fresh,
        bool downloads
    )
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired: 97, fresh);
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Standard(root, service);
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.Equal(downloads, await provider.DownloadNextAsync());

            int downloaded = downloads ? 1 : 0;
            Assert.Equal(downloaded, service.Downloaded.Count);
            // The expired backlog stays on disk as filler.
            Assert.Equal(
                97 + fresh + downloaded,
                Directory.GetFiles(Path.Combine(root, "Standard")).Length
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_LogsTheWaitingCountAndTheFreshCountAgainstTheLimit()
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired: 97, fresh: Limit);
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );
        var logger = new ListLogger();

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Standard(root, service, logger);
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(service.Listed);
            LogLine line = Assert.Single(logger.Lines);
            Assert.Equal(
                "102 replays waiting to be spectated (5 fresh, limit 5). Not downloading another.",
                line.Message
            );
            Assert.Equal(102, line.Values["Waiting"]);
            Assert.Equal(Limit, line.Values["Fresh"]);
            Assert.Equal(Limit, line.Values["Limit"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A listed replay already past its window would never count toward the limit, so it keeps
    /// the old cap on every waiting replay. Otherwise expired downloads would never stop.
    /// </summary>
    [Theory]
    [InlineData(Limit - 1, true)]
    [InlineData(Limit, false)]
    public async Task DownloadNextAsync_AnExpiredListingKeepsTheCapOnEveryWaitingReplay(
        int expired,
        bool downloads
    )
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired, fresh: 0);
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId => Replay(minId + 1, now - TimeSpan.FromDays(7))
        );

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false,
                maxReplayAge: TimeSpan.Zero
            );
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.Equal(downloads, await provider.DownloadNextAsync());

            Assert.Equal(downloads ? 1 : 0, service.Downloaded.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A replay whose game date cannot be read counts, as every waiting replay did.</summary>
    [Fact]
    public async Task DownloadNextAsync_CountsAWaitingReplayWhoseGameDateCannotBeRead()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );
        int reads = 0;

        try
        {
            WriteStandard(root, Backlog(expired: 0, fresh: Limit).Keys);
            HeroesProfileProvider provider = Standard(root, service);
            provider.UseReplayHeaders(_ =>
            {
                reads++;
                return null;
            });

            Assert.False(await provider.DownloadNextAsync());
            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(service.Downloaded);
            Assert.Equal(Limit, reads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Week-old replays from 6553xxxx, then fresh ones from 6558xxxx.</summary>
    private static Dictionary<int, DateTime> Backlog(int expired, int fresh)
    {
        DateTime now = DateTime.UtcNow;
        var played = new Dictionary<int, DateTime>();
        for (int index = 1; index <= expired; index++)
        {
            played[65530000 + index] = now - TimeSpan.FromDays(7);
        }

        for (int index = 1; index <= fresh; index++)
        {
            played[65580000 + index] = now - TimeSpan.FromHours(1);
        }

        return played;
    }

    private static void WriteStandard(string root, IEnumerable<int> ids)
    {
        string standard = Path.Combine(root, "Standard");
        Directory.CreateDirectory(standard);
        foreach (int id in ids)
        {
            File.WriteAllBytes(
                Path.Combine(
                    standard,
                    id + "_Storm League_Diamond 3_Cursed Hollow_abc.StormReplay"
                ),
                new byte[] { 1 }
            );
        }
    }

    private static Heroes.ReplayParser.Replay Header(
        string path,
        IReadOnlyDictionary<int, DateTime> played
    )
    {
        int id = int.Parse(Path.GetFileName(path).Split('_')[0]);
        return new Heroes.ReplayParser.Replay
        {
            Timestamp = played[id],
            ReplayVersion = "2.57.0.98285",
        };
    }

    private static HeroesProfileProvider Standard(
        string root,
        IHeroesProfileService service,
        ILogger<HeroesProfileProvider> logger = null
    )
    {
        HeroesProfileProvider provider = Provider(
            root,
            service,
            resume: null,
            CancellationToken.None,
            enableRequests: false,
            logger: logger
        );
        provider.UseInstalledVersions(() => new[] { Previous, Current });
        return provider;
    }

    private static ReplayListing Builds(params (int Id, string Version)[] rows)
    {
        string played = (DateTime.UtcNow - TimeSpan.FromHours(1)).ToString("yyyy-MM-dd HH:mm:ss");
        var replays = new List<HeroesProfileReplay>();
        foreach ((int id, string version) in rows)
        {
            replays.Add(
                new HeroesProfileReplay
                {
                    Id = id,
                    GameType = "Storm League",
                    GameVersion = version,
                    Map = "Cursed Hollow",
                    Fingerprint = "abc",
                    GameDate = played,
                }
            );
        }

        int highest = rows[rows.Length - 1].Id;
        return new ReplayListing(replays, hadRows: true, highestId: highest, nextAfter: highest);
    }

    private static ReplayListing Replay(int id, DateTime played) =>
        new(
            new[]
            {
                new HeroesProfileReplay
                {
                    Id = id,
                    GameType = "Storm League",
                    Map = "Cursed Hollow",
                    Fingerprint = "abc",
                    GameDate = played.ToString("yyyy-MM-dd HH:mm:ss"),
                },
            },
            hadRows: true,
            highestId: id,
            nextAfter: id
        );

    private static string NewRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-download-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static HeroesProfileProvider Provider(
        string root,
        IHeroesProfileService service,
        IHeroesProfileResume resume,
        CancellationToken token,
        bool enableRequests = true,
        TimeSpan? maxReplayAge = null,
        ILogger<HeroesProfileProvider> logger = null
    )
    {
        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
                MinReplayId = 65580000,
                GameTypes = new[] { "Storm League" },
                StandardMaxReplayAge = maxReplayAge ?? TimeSpan.FromHours(12),
                CachedReplayLimit = Limit,
            },
            ReplayMedia = new ReplayMediaPolicySettings
            {
                RecordingMode = ReplayRecordingMode.Selected,
                PublicationMode = ReplayPublicationMode.AllEligible,
                OrdinaryCandidateMaxAge = TimeSpan.FromDays(3),
            },
            StormReplay = new StormReplaySettings
            {
                Seperator = "_",
                WildCard = "*.StormReplay",
                FileExtension = ".StormReplay",
            },
            Twitch = new TwitchSettings { EnableRequests = enableRequests },
            Retention = new RetentionSettings { Enabled = false },
        };

        return new HeroesProfileProvider(
            logger ?? NullLogger<HeroesProfileProvider>.Instance,
            new NoLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new OneRequest(),
            service,
            new CancellationTokenProvider(token),
            settings,
            resume
        );
    }

    private sealed class OneRequest : IRequestQueue
    {
        public Task<RewardQueueItem> DequeueItemAsync() =>
            Task.FromResult(
                new RewardQueueItem
                {
                    HeroesProfileReplay = new HeroesProfileReplay
                    {
                        Id = 65582300,
                        GameType = "Storm League",
                        Rank = "Platinum 4",
                        Map = "Cursed Hollow",
                        Fingerprint = "abc",
                    },
                }
            );

        public Task<RewardResponse> EnqueueItemAsync(RewardRequest request) =>
            throw new NotSupportedException();

        public Task<int> GetItemsInQueue() => Task.FromResult(1);

        public Task<RewardQueueItem> FindByIndexAsync(int index) =>
            Task.FromResult<RewardQueueItem>(null);

        public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);

        public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);
    }

    private sealed record LogLine(string Message, IReadOnlyDictionary<string, object> Values);

    private sealed class ListLogger : ILogger<HeroesProfileProvider>
    {
        public List<LogLine> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            var values = new Dictionary<string, object>();
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (KeyValuePair<string, object> pair in pairs)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            Lines.Add(new LogLine(formatter(state, exception), values));
        }
    }

    private sealed class NoLoader : IReplayLoader
    {
        public Task<Heroes.ReplayParser.Replay> LoadAsync(string path) =>
            throw new NotSupportedException();
    }

    private sealed class ListedDownloads : IHeroesProfileService
    {
        private readonly int newest;
        private readonly Func<int, ReplayListing> listPage;

        public ListedDownloads(int newest, Func<int, ReplayListing> listPage)
        {
            this.newest = newest;
            this.listPage = listPage;
        }

        /// <summary>Every download asked for, including one <see cref="Refuse"/> fails.</summary>
        public List<int> Downloaded { get; } = new();

        /// <summary>The error a replay's download throws, or null to write it.</summary>
        public Func<int, Exception> Refuse { get; init; } = _ => null;

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloaded.Add(replayId);
            if (Refuse(replayId) is Exception refused)
            {
                throw refused;
            }

            await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
        }

        public int MaxIdLookups { get; private set; }

        public Task<int> GetMaxReplayIdAsync()
        {
            MaxIdLookups++;
            return Task.FromResult(newest);
        }

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => throw new NotSupportedException();

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            throw new NotSupportedException();

        public List<int> Listed { get; } = new();

        public Task<ReplayListing> ListPageAsync(int minId)
        {
            Listed.Add(minId);
            return Task.FromResult(listPage(minId));
        }

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class ScriptedDownloads : IHeroesProfileService
    {
        private readonly Func<Stream, CancellationToken, Task> write;

        public ScriptedDownloads(Func<Stream, CancellationToken, Task> write)
        {
            this.write = write;
        }

        public int Downloads { get; private set; }

        public bool FailFirst { get; set; }

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloads++;
            await write(destination, cancellationToken);
            if (FailFirst && Downloads == 1)
            {
                throw new IOException("connection reset");
            }
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
