using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.CLI.Commands.Update;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

/// <summary>
/// <c>update release-health</c> against a services.json, ready files, and a stop file in a temp
/// folder. Nothing reads the machine's own stack.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseHealthCheckTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Roles = { "spectate", "twitch", "download", "youtube" };

    [Fact]
    public void Wait_ReturnsHealthyOnceSpectateReachesTheClock()
    {
        string root = TempDir();
        var clock = new FakeClock(Since.AddMinutes(1));
        var output = new StringWriter();
        int waits = 0;
        try
        {
            WriteStack(root, spectateWorkAt: null);
            ReleaseHealthCheck check = Check(root, clock, output) with
            {
                Wait = _ =>
                {
                    waits++;
                    clock.Now += TimeSpan.FromSeconds(15);
                    if (waits == 3)
                    {
                        WriteStack(root, spectateWorkAt: clock.Now);
                    }
                },
            };

            ReleaseHealthResult result = check.WaitForVerdict(
                Since,
                TimeSpan.FromMinutes(20),
                CancellationToken.None
            );

            Assert.Equal(ReleaseHealthVerdict.Healthy, result.Verdict);
            Assert.Equal(3, waits);
            string[] lines = output
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains(
                "release health waiting: spectate has shown no match progress",
                lines[0]
            );
            Assert.EndsWith("release health healthy.", lines[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, ReleaseHealthVerdict.Inconclusive)]
    [InlineData("BuildNotInstalled", ReleaseHealthVerdict.Inconclusive)]
    [InlineData("LoadTimedOut", ReleaseHealthVerdict.Unhealthy)]
    public void Wait_WhenTheWindowClosesWithoutMatchProgress_BlamesOnlyFailedSessions(
        string outcome,
        ReleaseHealthVerdict expected
    )
    {
        string root = TempDir();
        var clock = new FakeClock(Since.AddMinutes(1));
        try
        {
            // Read back from the ready file, as the gate does on the stream PC.
            WriteStack(
                root,
                spectateWorkAt: Since.AddMinutes(-2),
                outcome == null ? null : new Dictionary<string, int> { [outcome] = 2 }
            );
            ReleaseHealthCheck check = Check(root, clock, TextWriter.Null) with
            {
                Wait = span => clock.Now += span,
                Poll = TimeSpan.FromMinutes(1),
            };

            ReleaseHealthResult result = check.WaitForVerdict(
                Since,
                TimeSpan.FromMinutes(20),
                CancellationToken.None
            );

            Assert.Equal(expected, result.Verdict);
            Assert.Equal((int)expected, result.ExitCode);
            Assert.Equal(Since.AddMinutes(20), clock.Now);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Check_AStopRequestIsNoVerdict_AndADeadRoleIsNotUp()
    {
        string root = TempDir();
        var clock = new FakeClock(Since.AddMinutes(30));
        try
        {
            WriteStack(root, spectateWorkAt: Since.AddMinutes(5));
            ReleaseHealthCheck check = Check(root, clock, TextWriter.Null);
            ReleaseHealthCheck youtubeGone = check with
            {
                ProcessNameOrNull = pid => pid == 104 ? null : "heroesreplay",
            };

            Assert.Equal(
                ReleaseHealthVerdict.Healthy,
                check.Check(Since, TimeSpan.FromMinutes(20)).Verdict
            );
            ReleaseHealthResult dead = youtubeGone.Check(Since, TimeSpan.FromMinutes(20));
            Assert.Equal(ReleaseHealthVerdict.Unhealthy, dead.Verdict);
            Assert.Contains("youtube is not running (failed).", dead.Problems);

            File.WriteAllText(check.StopPath, "stop");
            Assert.Equal(
                ReleaseHealthVerdict.Stopped,
                youtubeGone.Check(Since, TimeSpan.FromMinutes(20)).Verdict
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ReleaseHealthCheck Check(string root, FakeClock clock, TextWriter output) =>
        new()
        {
            LockPath = Path.Combine(root, "services.json"),
            ReadyDirectory = Path.Combine(root, "ready"),
            StopPath = Path.Combine(root, "services.stop"),
            ProcessNameOrNull = _ => "heroesreplay",
            Probe = _ => null,
            Time = clock,
            Out = output,
            Wait = _ => throw new InvalidOperationException("No wait expected."),
        };

    // Every role started 30 s after the install, heartbeats on an hour interval so it stays fresh.
    private static void WriteStack(
        string root,
        DateTimeOffset? spectateWorkAt,
        Dictionary<string, int> spectateOutcomes = null
    )
    {
        List<ServiceProcessRecord> records = Roles
            .Select(
                (role, index) =>
                    new ServiceProcessRecord
                    {
                        Name = role,
                        Pid = 101 + index,
                        Nonce = "n" + role,
                    }
            )
            .ToList();
        ServiceLockStore.Save(
            Path.Combine(root, "services.json"),
            new ServiceLock { StartedAt = Since, Processes = records }
        );
        foreach (ServiceProcessRecord record in records)
        {
            ServiceReadyFile.Report(
                new ServiceReadyReport
                {
                    Role = record.Name,
                    Nonce = record.Nonce,
                    Pid = record.Pid,
                    Readiness = ServiceReadiness.Ready,
                    ReadyAt = Since.AddSeconds(30),
                    HeartbeatAt = Since.AddSeconds(31),
                    HeartbeatIntervalSeconds = 3600,
                    LastSuccessfulWorkAt = record.Name == "spectate" ? spectateWorkAt : null,
                    SessionOutcomes = record.Name == "spectate" ? spectateOutcomes : null,
                },
                Path.Combine(root, "ready")
            );
        }
    }

    private static string TempDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-gate-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
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
