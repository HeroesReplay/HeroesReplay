using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySessionUploadTests
{
    [Fact]
    public async Task ProcessRecording_DryRunReceiptStaysOnTheReplayTrace()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-session-upload-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        string sessionPath = Path.Combine(directory, "replay-sessions.txt");
        using ActivityListener listener = Listen();
        ActivityTraceId replayTrace;
        using (Activity session = HeroesReplayTelemetry.BeginReplaySession(424242))
        {
            Assert.NotNull(session);
            replayTrace = session.TraceId;
            ReplaySessionFile.Publish(session, 424242, sessionPath);
        }

        string recordingPath = Path.Combine(directory, "match.mp4");
        await File.WriteAllBytesAsync(
            recordingPath,
            new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 }
        );
        var entry = new YouTubeEntry
        {
            Title = "Tomb of the Spider Queen - 424242",
            PrivacyStatus = "private",
            CategoryId = "20",
            ReplayId = 424242,
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, "youtube-entry.json"),
            JsonSerializer.Serialize(entry)
        );

        var log = new ReceiptLogger();
        var uploader = new YouTubeUploader(
            log,
            new AppSettings
            {
                YouTube = new YouTubeSettings
                {
                    DryRun = true,
                    Enabled = true,
                    EntryFileName = "youtube-entry.json",
                    EntryFileNameUploaded = "youtube-entry-uploaded.json",
                    ReadyStableReads = 1,
                    ReadyPollMilliseconds = 20,
                },
                Location = new LocationSettings { DataDirectory = directory },
            },
            new CancellationTokenSource()
        )
        {
            ReplaySessionFilePath = sessionPath,
        };

        using var ambient = new Activity("process");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();
        bool receiptWritten = false;
        try
        {
            await uploader.ProcessRecording(recordingPath);
            receiptWritten = File.Exists(Path.Combine(directory, "youtube-dry-run.json"));
        }
        finally
        {
            ambient.Stop();
            Directory.Delete(directory, recursive: true);
        }

        Assert.True(receiptWritten);
        Assert.Equal(replayTrace, log.ReceiptTrace);
        Assert.NotEqual(ambient.TraceId, log.ReceiptTrace);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class ReceiptLogger : ILogger<YouTubeUploader>
    {
        public ActivityTraceId? ReceiptTrace { get; private set; }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            string message = formatter(state, exception);
            if (
                message != null
                && message.Contains("Dry run saved", StringComparison.Ordinal)
                && Activity.Current != null
            )
            {
                ReceiptTrace = Activity.Current.TraceId;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
