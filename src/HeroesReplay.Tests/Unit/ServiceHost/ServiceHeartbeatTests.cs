using System;
using System.IO;
using System.Threading;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceHeartbeatTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_WritesReadyThenAHeartbeatThatFollowsIt()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("download", "beat1"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);

            ServiceReadyReport read = Read("download", "beat1", root);
            Assert.Equal("download", read.Role);
            Assert.Equal("9.9.9", read.Version);
            Assert.Equal(@"C:\heroesreplay\app\heroesreplay.exe", read.ExecutablePath);
            Assert.Equal(4242, read.Pid);
            Assert.Equal(ServiceReadiness.Ready, read.Readiness);
            Assert.Equal(Start, read.ReadyAt);
            Assert.Equal(3600, read.HeartbeatIntervalSeconds);
            Assert.True(ServiceChildHeartbeat.FollowsReady(read));
            Assert.Null(read.LastSuccessfulWorkAt);
            Assert.Null(read.LastError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Beat_AdvancesWithTheClockAndCarriesWorkAndError()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("youtube", "beat2"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);

            clock.Now = Start.AddSeconds(10);
            heartbeat.Work();
            clock.Now = Start.AddSeconds(12);
            heartbeat.Error("Could not upload C:\\a.mp4 ?access_token=abc123&part=x");
            clock.Now = Start.AddSeconds(15);
            heartbeat.Beat();

            ServiceReadyReport read = Read("youtube", "beat2", root);
            Assert.Equal(Start.AddSeconds(15), read.HeartbeatAt);
            Assert.Equal(Start.AddSeconds(10), read.LastSuccessfulWorkAt);
            Assert.Equal(Start.AddSeconds(12), read.LastError.At);
            Assert.Contains("access_token=[redacted]", read.LastError.Message);
            Assert.DoesNotContain("abc123", read.LastError.Message);

            clock.Now = Start.AddSeconds(30);
            heartbeat.Beat();
            Assert.Equal(Start.AddSeconds(30), Read("youtube", "beat2", root).HeartbeatAt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Dispose_WithoutAStopRequest_WritesExited()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            var heartbeat = new ServiceHeartbeat(
                Identity("spectate", "beat3"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);
            clock.Now = Start.AddMinutes(1);
            heartbeat.Dispose();

            ServiceReadyReport read = Read("spectate", "beat3", root);
            Assert.Equal(ServiceReadiness.Exited, read.Readiness);
            Assert.Equal(Start.AddMinutes(1), read.HeartbeatAt);

            // A late timer tick after disposal does not rewrite the file.
            clock.Now = Start.AddMinutes(2);
            heartbeat.Beat();
            Assert.Equal(Start.AddMinutes(1), Read("spectate", "beat3", root).HeartbeatAt);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StopRequest_WritesStoppingAndDisposalKeepsIt()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        using var stop = new CancellationTokenSource();
        try
        {
            var heartbeat = new ServiceHeartbeat(
                Identity("twitch", "beat4"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(stop.Token);
            clock.Now = Start.AddSeconds(5);
            stop.Cancel();

            ServiceReadyReport stopping = Read("twitch", "beat4", root);
            Assert.Equal(ServiceReadiness.Stopping, stopping.Readiness);
            Assert.Equal(Start.AddSeconds(5), stopping.HeartbeatAt);

            heartbeat.Dispose();
            Assert.Equal(ServiceReadiness.Stopping, Read("twitch", "beat4", root).Readiness);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Timer_RefreshesTheHeartbeatOnItsOwn()
    {
        string root = TempDir();
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("download", "beat5"),
                TimeSpan.FromMilliseconds(50),
                TimeProvider.System,
                root
            );
            heartbeat.Start(CancellationToken.None);
            DateTimeOffset first = Read("download", "beat5", root).HeartbeatAt.Value;

            // The timer runs on the thread pool; only a dead timer takes this long (#331).
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            DateTimeOffset? later = first;
            while (later <= first && DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(50);
                later = Read("download", "beat5", root)?.HeartbeatAt ?? first;
            }

            Assert.True(later > first, "The heartbeat did not refresh on its interval.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordWorkAndError_ReachTheInstalledHeartbeatOnly()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("download", "beat6"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);
            heartbeat.Install();

            clock.Now = Start.AddSeconds(3);
            ServiceHeartbeat.RecordWork();
            using (var provider = new ServiceHeartbeatLoggerProvider())
            {
                ILogger logger = provider.CreateLogger("Download");
                logger.LogWarning("A warning is not an error.");
                Assert.Null(heartbeat.Snapshot().LastError);
                logger.LogError(
                    new InvalidOperationException("boom-149"),
                    "Heroes Profile download failed."
                );
            }

            ServiceReadyReport snapshot = heartbeat.Snapshot();
            Assert.NotNull(snapshot.LastSuccessfulWorkAt);
            Assert.Equal(
                "Heroes Profile download failed. (InvalidOperationException: boom-149)",
                snapshot.LastError.Message
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LaunchPhase_IsWrittenAndEndsOnMatchProgressTheReportOrTheSessionEnd()
    {
        string root = TempDir();
        var clock = new FakeClock(Start);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("spectate", "beat7"),
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Start(CancellationToken.None);
            Assert.Null(heartbeat.Snapshot().LaunchingSince);

            clock.Now = Start.AddMinutes(1);
            heartbeat.Launching();
            heartbeat.Beat();
            Assert.Equal(Start.AddMinutes(1), Read("spectate", "beat7", root).LaunchingSince);

            // Game data still downloading starts the phase over.
            clock.Now = Start.AddMinutes(5);
            heartbeat.Launching();
            Assert.Equal(Start.AddMinutes(5), heartbeat.Snapshot().LaunchingSince);

            heartbeat.Work();
            Assert.Null(heartbeat.Snapshot().LaunchingSince);

            heartbeat.Launching();
            heartbeat.LaunchEnded();
            Assert.Null(heartbeat.Snapshot().LaunchingSince);

            heartbeat.Launching();
            heartbeat.Session(matchProgress: false, "BuildNotInstalled");
            Assert.Null(heartbeat.Snapshot().LaunchingSince);
            Assert.Equal(1, heartbeat.Snapshot().SessionsWithoutProgress);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoggerProvider_PassesErrorsAndCriticalsOnly()
    {
        string recorded = null;
        using var provider = new ServiceHeartbeatLoggerProvider(message => recorded = message);
        ILogger logger = provider.CreateLogger("Engine");

        logger.LogInformation("fine");
        Assert.Null(recorded);
        logger.LogCritical("Spectate hit an error and is still running.");
        Assert.Equal("Spectate hit an error and is still running.", recorded);
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Theory]
    [InlineData("GET /replays?api_token=s3cret&x=1", "api_token=[redacted]")]
    [InlineData("Authorization: Bearer abc.def-ghi", "Bearer [redacted]")]
    [InlineData("password=hunter2", "password=[redacted]")]
    public void Redact_HidesTokensInErrors(string message, string expected)
    {
        string redacted = ServiceHeartbeat.Redact(message);
        Assert.Contains(expected, redacted);
        Assert.DoesNotContain("s3cret", redacted);
        Assert.DoesNotContain("abc.def-ghi", redacted);
        Assert.DoesNotContain("hunter2", redacted);
    }

    [Theory]
    [InlineData("Check the v1 Bearer key.")]
    [InlineData("This is not the v1 Bearer key, it is the uploader key.")]
    [InlineData("Bearer tokens expire.")]
    public void Redact_KeepsProseThatOnlyNamesACredential(string message)
    {
        Assert.Equal(message, ServiceHeartbeat.Redact(message));
    }

    [Fact]
    public void Redact_CapsLongErrors()
    {
        string redacted = ServiceHeartbeat.Redact(new string('x', 2000));
        Assert.True(redacted.Length <= 403);
        Assert.EndsWith("...", redacted);
    }

    private static ServiceReadyReport Identity(string role, string nonce) =>
        new()
        {
            Role = role,
            Nonce = nonce,
            Version = "9.9.9",
            ExecutablePath = @"C:\heroesreplay\app\heroesreplay.exe",
            Pid = 4242,
        };

    private static ServiceReadyReport Read(string role, string nonce, string root) =>
        ServiceReadyFile.TryRead(new ServiceProcessRecord { Name = role, Nonce = nonce }, root);

    private static string TempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-heartbeat-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
