using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.PubSub.Events;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RequestQueueTests
{
    [Fact]
    public async Task Dequeue_WhenMutexIsHeld_DoesNotLogAnError()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-queue-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        string mutexName = @"Local\HeroesReplay.RequestQueue.Test." + Guid.NewGuid().ToString("N");
        var logger = new RecordingLogger();
        var blocker = new Mutex(false, mutexName);
        Assert.True(blocker.WaitOne(TimeSpan.FromSeconds(2)));
        RequestQueue queue = null;
        try
        {
            File.WriteAllText(Path.Combine(directory, "requests.json"), "[]");
            queue = new RequestQueue(
                logger,
                heroesProfileService: null,
                new AppSettings
                {
                    Location = new LocationSettings { DataDirectory = directory },
                    Twitch = new TwitchSettings
                    {
                        QueueFileName = "requests.json",
                        FailedFileName = "failed-requests.json",
                    },
                },
                TimeSpan.FromMilliseconds(200),
                mutexName,
                mutexName + ".failed"
            );

            Assert.Null(await queue.PeekDownloadAsync(DateTimeOffset.UtcNow));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
            Assert.DoesNotContain(logger.Entries, entry => entry.Exception != null);
            Assert.Contains(
                logger.Entries,
                entry =>
                    entry.Level == LogLevel.Warning
                    && entry.Message.Contains("busy", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            try
            {
                blocker.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The queue wait can abandon this named mutex on the waiter thread.
            }

            blocker.Dispose();
            queue?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Enqueue_SameRedemption_DoesNotQueueTwice()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            var redemptionId = Guid.NewGuid();
            RewardRequest request = FilterRequest(redemptionId);
            RewardResponse first = await queue.EnqueueItemAsync(request);
            RewardResponse second = await queue.EnqueueItemAsync(request);

            Assert.True(first.Success);
            Assert.False(first.Duplicate);
            Assert.True(second.Duplicate);
            Assert.False(second.Success);
            Assert.Equal(1, await queue.GetItemsInQueue());
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Enqueue_DifferentRedemptions_BothQueue()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            Assert.Equal(2, await queue.GetItemsInQueue());
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Enqueue_SameReplayIdRedemption_DoesNotQueueTwice()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            var request = new RewardRequest(
                "saltysadism",
                Guid.NewGuid(),
                "ReplayId",
                42,
                rank: null,
                map: null,
                GameType.StormLeague
            );
            RewardResponse first = await queue.EnqueueItemAsync(request);
            RewardResponse second = await queue.EnqueueItemAsync(request);

            Assert.True(first.Success);
            Assert.Contains("42", first.Message, StringComparison.Ordinal);
            Assert.True(second.Duplicate);
            Assert.Equal(1, await queue.GetItemsInQueue());
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_WritesTheQueuePageWithTheConfiguredRewardTitles()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(
            directory,
            new FixedRewards(
                new SupportedReward(RewardType.QM, "Random (QM)"),
                new SupportedReward(RewardType.ReplayId, "ReplayId")
            )
        );
        try
        {
            string html = File.ReadAllText(Path.Combine(directory, QueueBoard.FileName));

            Assert.Contains("<span class=\"label\">Random (QM)</span> play a random match.", html);
            Assert.Contains("Redeem <span class=\"label\">ReplayId</span>", html);
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_RewardsNotLoaded_StillWritesTheQueuePage()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory, new FixedRewards(null));
        try
        {
            string html = File.ReadAllText(Path.Combine(directory, QueueBoard.FileName));

            Assert.Contains("Pick a random match, a map, or a rank.", html);
            Assert.Contains("Redeem the replay reward", html);
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>#351: a request leaves the queue only after its replay is on disk.</summary>
    [Fact]
    public async Task CompleteDownload_PublishThatThrows_KeepsTheRequestQueued()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            RewardQueueItem item = await queue.PeekDownloadAsync(DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<IOException>(() =>
                queue.CompleteDownloadAsync(item, () => throw new IOException("disk full"))
            );
            Assert.Equal(1, await queue.GetItemsInQueue());

            bool published = false;
            Assert.Equal(
                RequestCompletion.Completed,
                await queue.CompleteDownloadAsync(item, () => published = true)
            );
            Assert.True(published);
            Assert.Equal(0, await queue.GetItemsInQueue());
            Assert.Equal(
                RequestCompletion.NotQueued,
                await queue.CompleteDownloadAsync(item, () => throw new InvalidOperationException())
            );
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RetryDownloadLater_IsNotDueUntilItsBackoffAndCountsAttempts()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RewardQueueItem item = await queue.PeekDownloadAsync(now);

            RequestDownload first = await queue.RetryDownloadLaterAsync(item, "HTTP 503", now);
            RequestDownload second = await queue.RetryDownloadLaterAsync(item, "HTTP 502", now);

            Assert.Equal(1, first.Attempts);
            Assert.Equal(2, second.Attempts);
            Assert.Equal("HTTP 502", second.LastError);
            Assert.Equal(now + RequestDownloadRetry.Delay(2), second.NextAttemptAt);
            Assert.Null(await queue.PeekDownloadAsync(now + RequestDownloadRetry.Delay(1)));
            RewardQueueItem due = await queue.PeekDownloadAsync(
                now + RequestDownloadRetry.Delay(2)
            );
            Assert.Equal(2, due.Download.Attempts);
            Assert.Equal(1, await queue.GetItemsInQueue());
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A pass killed after it kept the failure but before the request left the queue runs again.
    /// The failed file keeps one record of it.
    /// </summary>
    [Fact]
    public async Task FailDownload_TwiceForTheSameRequest_KeepsOneFailedRecord()
    {
        string directory = TempDirectory();
        RequestQueue queue = CreateQueue(directory);
        try
        {
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RewardQueueItem item = await queue.PeekDownloadAsync(now);

            Assert.True(await queue.FailDownloadAsync(item, "gone", refundRequested: true, now));
            Assert.True(await queue.FailDownloadAsync(item, "gone", refundRequested: true, now));

            Assert.Equal(0, await queue.GetItemsInQueue());
            RewardQueueItem failed = Assert.Single(
                RequestQueue.Snapshot(Path.Combine(directory, "failed-requests.json"))
            );
            Assert.Equal("gone", failed.Download.FailureReason);
            Assert.Contains(
                "Could not play",
                File.ReadAllText(Path.Combine(directory, QueueBoard.FileName))
            );
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A queue written before #351 has no download state and is still read.</summary>
    [Fact]
    public async Task QueueWrittenByAnOlderBuild_IsStillDue()
    {
        string directory = TempDirectory();
        File.WriteAllText(
            Path.Combine(directory, "requests.json"),
            """
            [
              {
                "Request": { "RedemptionId": "0a018521-19d9-437d-904b-4096c971d461", "Login": "zemill", "RewardTitle": "ReplayId", "ReplayId": 65625279 },
                "HeroesProfileReplay": { "replayID": 65625279, "game_map": "Cursed Hollow" }
              }
            ]
            """
        );
        RequestQueue queue = CreateQueue(directory);
        try
        {
            RewardQueueItem item = await queue.PeekDownloadAsync(DateTimeOffset.UtcNow);

            Assert.Equal(65625279, item.HeroesProfileReplay.Id);
            Assert.Null(item.Download);

            // Saved again by this build, a request with no failed download stays in the old shape.
            Assert.True((await queue.EnqueueItemAsync(FilterRequest(Guid.NewGuid()))).Success);
            Assert.Equal(2, await queue.GetItemsInQueue());
            Assert.DoesNotContain(
                "\"Download\"",
                File.ReadAllText(Path.Combine(directory, "requests.json"))
            );
        }
        finally
        {
            queue.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecentFailures_KeepsTheLastDayNewestFirstWithAReason()
    {
        DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        RewardQueueItem Failed(string login, TimeSpan ago, string reason = "gone") =>
            new()
            {
                Request = new RewardRequest { Login = login },
                Download = new RequestDownload { FailedAt = now - ago, FailureReason = reason },
            };

        var failed = new[]
        {
            new RewardQueueItem { Request = new RewardRequest { Login = "enqueue-time" } },
            Failed("old", TimeSpan.FromHours(25)),
            Failed("older", TimeSpan.FromHours(2)),
            Failed("newest", TimeSpan.FromMinutes(5)),
            Failed("no-reason", TimeSpan.FromMinutes(1), reason: null),
        };

        IReadOnlyList<RewardQueueItem> recent = RequestQueue.RecentFailures(failed, now);

        Assert.Equal(new[] { "newest", "older" }, recent.Select(item => item.Request.Login));
        Assert.Single(RequestQueue.RecentFailures(failed, now, limit: 1));
        Assert.Empty(RequestQueue.RecentFailures(null, now));
    }

    private sealed class FixedRewards : ICustomRewardsHolder
    {
        private readonly SupportedReward[] rewards;

        public FixedRewards(params SupportedReward[] rewards)
        {
            this.rewards = rewards;
        }

        public List<SupportedReward> Rewards =>
            rewards == null
                ? throw new InvalidOperationException("Maps are not loaded.")
                : new List<SupportedReward>(rewards);

        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
        {
            reward = null;
            return false;
        }
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-queue-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static RequestQueue CreateQueue(string directory, ICustomRewardsHolder rewards = null)
    {
        return new RequestQueue(
            new RecordingLogger(),
            new StubHeroesProfile(),
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                Twitch = new TwitchSettings
                {
                    QueueFileName = "requests.json",
                    FailedFileName = "failed-requests.json",
                },
            },
            TimeSpan.FromSeconds(5),
            @"Local\HeroesReplay.RequestQueue.Test." + Guid.NewGuid().ToString("N"),
            @"Local\HeroesReplay.FailedRequests.Test." + Guid.NewGuid().ToString("N"),
            rewards
        );
    }

    private static RewardRequest FilterRequest(Guid redemptionId)
    {
        return new RewardRequest(
            "saltysadism",
            redemptionId,
            "Braxis Holdout (Rank SL)",
            replayId: null,
            GameRank.Gold,
            "Braxis Holdout",
            GameType.StormLeague
        );
    }

    private sealed class StubHeroesProfile : IHeroesProfileService
    {
        public Task<int> GetMaxReplayIdAsync() => throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        )
        {
            return Task.FromResult<IEnumerable<HeroesProfileReplay>>(
                new[]
                {
                    new HeroesProfileReplay
                    {
                        Id = 10,
                        Map = "Braxis Holdout",
                        Rank = "Gold",
                        Downloadable = true,
                    },
                    new HeroesProfileReplay
                    {
                        Id = 11,
                        Map = "Braxis Holdout",
                        Rank = "Gold",
                        Downloadable = true,
                    },
                }
            );
        }

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId)
        {
            return Task.FromResult(
                new HeroesProfileReplay
                {
                    Id = replayId,
                    Map = "Braxis Holdout",
                    Downloadable = true,
                }
            );
        }

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            throw new NotSupportedException();

        public Task<ReplayListing> ListPageAsync(int minId) => throw new NotSupportedException();

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : ILogger<RequestQueue>
    {
        public List<Entry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            Entries.Add(new Entry(logLevel, exception, formatter(state, exception)));
        }

        public sealed record Entry(LogLevel Level, Exception Exception, string Message);
    }
}
