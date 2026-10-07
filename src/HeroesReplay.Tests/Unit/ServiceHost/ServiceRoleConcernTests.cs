using System;
using System.IO;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>#250: a running role reports a concern about its own work, and status shows it degraded.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceRoleConcernTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Concern_MakesALiveYouTubeRoleDegradedWithItsCode()
    {
        ServiceReadyReport beat = Beat();
        beat.Concern = new ServiceRoleConcern
        {
            Code = YouTubeUploaderHealth.QuotaBlockedCode,
            Cause =
                "4 recording(s) wait for upload and the YouTube upload quota holds new uploads.",
            Since = Now.AddMinutes(-90),
        };

        ServiceRoleHealth health = Classify(beat);

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Equal(ServiceHealthCodes.Degraded, health.Code);
        Assert.Equal(YouTubeUploaderHealth.QuotaBlockedCode, health.CauseCode);
        Assert.Contains("4 recording(s)", health.Cause, StringComparison.Ordinal);
        Assert.Contains("For 1h 30m.", health.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void NoConcern_StaysReady()
    {
        Assert.Equal(ServiceRoleState.Ready, Classify(Beat()).State);
    }

    [Fact]
    public void Heartbeat_KeepsTheFirstTimeOfTheSameConcernAndClearsIt()
    {
        var clock = new MovingClock { Now = Now };
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-concern-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                new ServiceReadyReport
                {
                    Role = "youtube",
                    Nonce = "concern1",
                    Version = "1.2.3",
                    Pid = 70,
                },
                TimeSpan.FromHours(1),
                clock,
                root
            );

            heartbeat.Concern(YouTubeUploaderHealth.NotPublishingCode, "nothing public");
            clock.Now = Now.AddHours(2);
            heartbeat.Concern(YouTubeUploaderHealth.NotPublishingCode, "still nothing public");
            ServiceRoleConcern held = heartbeat.Snapshot().Concern;
            heartbeat.Concern(null, null);

            Assert.Equal(Now, held.Since);
            Assert.Equal("still nothing public", held.Cause);
            Assert.Null(heartbeat.Snapshot().Concern);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ServiceRoleHealth Classify(ServiceReadyReport beat) =>
        ServiceHealthClassifier.Classify(
            "youtube",
            new ServiceProcessRecord
            {
                Name = "youtube",
                Pid = 70,
                Nonce = "nonce",
                Arguments = "youtube",
                Version = "1.2.3",
            },
            running: true,
            beat,
            stopRequested: false,
            Now,
            new ServiceHealthSettings()
        );

    private static ServiceReadyReport Beat() =>
        new()
        {
            Version = "1.2.3",
            Readiness = ServiceReadiness.Ready,
            ReadyAt = Now.AddHours(-2),
            HeartbeatAt = Now.AddSeconds(-3),
            HeartbeatIntervalSeconds = 15,
            LastSuccessfulWorkAt = Now.AddMinutes(-1),
        };

    private sealed class MovingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
