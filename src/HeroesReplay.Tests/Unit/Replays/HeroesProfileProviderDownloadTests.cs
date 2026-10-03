using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using Microsoft.Extensions.Logging.Abstractions;
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
        TimeSpan? maxReplayAge = null
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
            NullLogger<HeroesProfileProvider>.Instance,
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

        public List<int> Downloaded { get; } = new();

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloaded.Add(replayId);
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

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(listPage(minId));

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
