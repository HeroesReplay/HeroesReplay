using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Queue;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Queue;

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

            Assert.Null(await queue.DequeueItemAsync());
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

    private static string TempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-queue-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static RequestQueue CreateQueue(string directory)
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
            @"Local\HeroesReplay.FailedRequests.Test." + Guid.NewGuid().ToString("N")
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
