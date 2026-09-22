using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
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
