using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>#305: each role probes its live dependency before ready, then on a bounded interval.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceDependencyMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstAsync_ReturnsTheProbeResult_AndLogsAFailureOnceWithItsFix()
    {
        var probe = new FakeProbe(Rejected());
        var log = new ListLogger();
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            log,
            serviceRole: true
        );

        ServiceDependencyResult first = await monitor.FirstAsync(CancellationToken.None);
        await monitor.ProbeAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Rejected, first.State);
        Assert.Equal("download.heroesprofile_rejected", first.Code);
        Assert.Equal(2, probe.Calls);
        string warning = Assert.Single(log.Warnings);
        Assert.Contains("download.heroesprofile_rejected", warning);
        Assert.Contains("does not restart it", warning);
        Assert.Contains("Fix: rotate the key", warning);
    }

    [Fact]
    public async Task APassingProbeAfterAFailure_LogsThatTheCodeIsCleared()
    {
        var probe = new FakeProbe(Rejected(), Ok());
        var log = new ListLogger();
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            log,
            serviceRole: true
        );

        await monitor.FirstAsync(CancellationToken.None);
        ServiceDependencyResult second = await monitor.ProbeAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Ok, second.State);
        Assert.Contains(log.Information, line => line.Contains("is cleared"));
    }

    [Fact]
    public async Task AProbeThatHangs_IsUnreachableWithinTheBound_NotAnException()
    {
        var probe = new FakeProbe { Hang = true };
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings { DependencyProbeTimeout = TimeSpan.FromSeconds(1) },
            serviceRole: true
        );

        DateTimeOffset started = DateTimeOffset.UtcNow;
        ServiceDependencyResult result = await monitor.FirstAsync(CancellationToken.None);

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Equal(FakeProbe.UnreachableCode, result.Code);
        Assert.Contains("did not answer within 1s", result.Cause);
    }

    [Fact]
    public async Task AProbeThatThrows_IsUnreachable()
    {
        var probe = new FakeProbe { Throw = new InvalidOperationException("socket closed") };
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            serviceRole: true
        );

        ServiceDependencyResult result = await monitor.FirstAsync(CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unreachable, result.State);
        Assert.Contains("socket closed", result.Cause);
    }

    [Fact]
    public async Task AStopDuringTheProbe_ReportsNothing()
    {
        using var stop = new CancellationTokenSource();
        var probe = new FakeProbe { Hang = true };
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            serviceRole: true
        );

        Task<ServiceDependencyResult> first = monitor.FirstAsync(stop.Token);
        stop.Cancel();

        Assert.Null(await first);
    }

    [Fact]
    public async Task NoServiceRole_OrProbesOff_SendsNoProbe()
    {
        var probe = new FakeProbe(Ok());

        Assert.Null(
            await new ServiceDependencyMonitor(
                probe,
                new ServiceHealthSettings(),
                serviceRole: false
            ).FirstAsync(CancellationToken.None)
        );
        Assert.Null(
            await new ServiceDependencyMonitor(
                probe,
                new ServiceHealthSettings { DependencyProbes = false },
                serviceRole: true
            ).FirstAsync(CancellationToken.None)
        );
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task AnUnusedDependency_IsReportedOnce_AndNeverProbed()
    {
        var probe = new FakeProbe(Ok()) { NotUsed = "YouTube:DryRun is on." };
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            serviceRole: true
        );
        var recorded = new List<ServiceDependencyResult>();

        ServiceDependencyResult first = await monitor.FirstAsync(CancellationToken.None);
        await monitor.RunAsync(recorded.Add, CancellationToken.None);

        Assert.Equal(ServiceDependencyStates.Unused, first.State);
        Assert.Equal("YouTube:DryRun is on.", first.Cause);
        Assert.False(first.Failed);
        Assert.Empty(recorded);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task RunAsync_ProbesAgainAfterTheWait_AndRecordsEachResult()
    {
        using var stop = new CancellationTokenSource();
        var probe = new FakeProbe(Rejected(), Ok(), Ok());
        var recorded = new List<ServiceDependencyResult>();
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            time: new InstantDelays(),
            serviceRole: true
        );

        await monitor.FirstAsync(stop.Token);
        await monitor.RunAsync(
            result =>
            {
                recorded.Add(result);
                if (recorded.Count == 2)
                {
                    stop.Cancel();
                }
            },
            stop.Token
        );

        Assert.Equal(3, probe.Calls);
        Assert.All(recorded, result => Assert.Equal(ServiceDependencyStates.Ok, result.State));
    }

    /// <summary>#358: the downloader pauses its replay list on what the probe finds.</summary>
    [Fact]
    public async Task Watch_HandsEachResultToTheObserver()
    {
        var probe = new FakeProbe(Rejected(), Ok());
        var observed = new TaskCompletionSource<ServiceDependencyResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var monitor = new ServiceDependencyMonitor(
            probe,
            new ServiceHealthSettings(),
            time: new InstantDelays(),
            serviceRole: true
        );

        await monitor.FirstAsync(CancellationToken.None);
        using (monitor.Watch(CancellationToken.None, result => observed.TrySetResult(result)))
        {
            ServiceDependencyResult first = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(ServiceDependencyStates.Ok, first.State);
        }
    }

    [Fact]
    public void TheWaitIsShorterAfterAFailure_AndNeverUnderAMinute()
    {
        var settings = new ServiceHealthSettings();
        Assert.Equal(TimeSpan.FromMinutes(10), settings.NextDependencyProbe(failed: false));
        Assert.Equal(TimeSpan.FromMinutes(2), settings.NextDependencyProbe(failed: true));

        var fast = new ServiceHealthSettings
        {
            DependencyProbeInterval = TimeSpan.FromSeconds(5),
            DependencyRetryInterval = TimeSpan.FromSeconds(1),
            DependencyProbeTimeout = TimeSpan.FromMinutes(10),
        };
        Assert.Equal(TimeSpan.FromMinutes(1), fast.NextDependencyProbe(failed: false));
        Assert.Equal(TimeSpan.FromMinutes(1), fast.NextDependencyProbe(failed: true));
        Assert.Equal(TimeSpan.FromSeconds(60), fast.DependencyProbeBound());
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            new ServiceHealthSettings
            {
                DependencyProbeTimeout = TimeSpan.Zero,
            }.DependencyProbeBound()
        );
    }

    [Fact]
    public void Heartbeat_WritesTheProbeInTheReadyFile_KeepsSince_AndAPassClearsIt()
    {
        var clock = new MovingClock { Now = Now };
        string root = Path.Combine(Path.GetTempPath(), "hr-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                new ServiceReadyReport
                {
                    Role = "download",
                    Nonce = "probe1",
                    Version = "1.2.3",
                    Pid = 70,
                },
                TimeSpan.FromHours(1),
                clock,
                root
            );
            heartbeat.Dependency(Rejected());
            heartbeat.Start(CancellationToken.None);
            clock.Now = Now.AddMinutes(2);
            heartbeat.Dependency(Rejected());
            ServiceRoleDependency held = heartbeat.Snapshot().Dependency;
            ServiceReadyReport file = ServiceReadyFile.TryRead(
                new ServiceProcessRecord { Name = "download", Nonce = "probe1" },
                root
            );

            clock.Now = Now.AddMinutes(4);
            heartbeat.Dependency(Ok());
            ServiceRoleDependency cleared = heartbeat.Snapshot().Dependency;

            Assert.Equal("download.heroesprofile_rejected", file.Dependency.Code);
            Assert.Equal(ServiceDependencyStates.Rejected, file.Dependency.State);
            Assert.Equal("rotate the key", file.Dependency.Remediation);
            Assert.Equal(Now, held.Since);
            Assert.Equal(Now.AddMinutes(2), held.CheckedAt);
            Assert.Equal(ServiceDependencyStates.Ok, cleared.State);
            Assert.Null(cleared.Code);
            Assert.Equal(Now.AddMinutes(4), cleared.Since);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Heartbeat_RedactsATokenInTheCause()
    {
        using var heartbeat = new ServiceHeartbeat(
            new ServiceReadyReport { Role = "twitch", Nonce = "probe2" },
            TimeSpan.FromHours(1),
            new MovingClock { Now = Now },
            Path.Combine(Path.GetTempPath(), "hr-probe-" + Guid.NewGuid().ToString("N"))
        );

        heartbeat.Dependency(
            ServiceDependencyResult.Rejected(
                "Twitch token",
                "twitch.token_invalid",
                "refused Bearer abcdef0123456789secret",
                "fix"
            )
        );

        Assert.DoesNotContain("abcdef0123456789secret", heartbeat.Snapshot().Dependency.Cause);
    }

    internal static ServiceDependencyResult Rejected() =>
        ServiceDependencyResult.Rejected(
            "Heroes Profile API",
            "download.heroesprofile_rejected",
            "Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP 401).",
            "rotate the key"
        );

    internal static ServiceDependencyResult Ok() =>
        ServiceDependencyResult.Ok("Heroes Profile API", "Heroes Profile accepted the API key.");

    private sealed class FakeProbe : IServiceDependencyProbe
    {
        public const string UnreachableCode = "download.heroesprofile_unreachable";
        private readonly Queue<ServiceDependencyResult> results;

        public FakeProbe(params ServiceDependencyResult[] results)
        {
            this.results = new Queue<ServiceDependencyResult>(results);
        }

        public int Calls { get; private set; }
        public bool Hang { get; init; }
        public Exception Throw { get; init; }
        public string NotUsed { get; init; }
        public string Dependency => "Heroes Profile API";
        public string NotUsedReason => NotUsed;

        public ServiceDependencyResult Unreachable(string cause) =>
            ServiceDependencyResult.Unreachable(Dependency, UnreachableCode, cause, "wait");

        public async Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw != null)
            {
                throw Throw;
            }

            if (Hang)
            {
                // Ignores its token, as a stuck socket would; the monitor's own bound ends it.
                await Task.Delay(Timeout.Infinite, CancellationToken.None);
            }

            return results.Count > 1 ? results.Dequeue() : results.Peek();
        }
    }

    /// <summary>The probe intervals (a minute or more) finish at once; the probe bound does not.</summary>
    private sealed class InstantDelays : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object state,
            TimeSpan dueTime,
            TimeSpan period
        ) =>
            System.CreateTimer(
                callback,
                state,
                dueTime >= TimeSpan.FromMinutes(1) ? TimeSpan.Zero : dueTime,
                period
            );
    }

    private sealed class MovingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Warnings { get; } = new();
        public List<string> Information { get; } = new();

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
            string line = formatter(state, exception);
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(line);
            }
            else if (logLevel == LogLevel.Information)
            {
                Information.Add(line);
            }
        }
    }
}
