using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// #398: the supervisor watches OBS. A missing obs64 is started, a hung one is closed, killed,
/// and started, a live stream is never touched, and nothing happens while streaming is not
/// desired or a stop is under way. Every OBS step is a fake.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsWatchdogTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);
    private const string Exe = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";

    /// <summary>The detached start of OBS with the profile and collection only (#409).</summary>
    private const string Launched =
        "cmd /d /c start \"\" /D \"C:\\Program Files\\obs-studio\\bin\\64bit\" \"C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe\" --profile \"HeroesReplay\" --collection \"HeroesReplay\"";

    [Fact]
    public void AMissingObs_IsStartedAtOnce_WithTheProfileAndCollection_NeverTheStream()
    {
        var obs = new FakeObs();
        ObsWatchdog watchdog = obs.Watchdog();

        Assert.Equal(ObsWatchdogAction.Start, watchdog.Tick(Start, stopRequested: false));

        Assert.Equal(new[] { Launched }, obs.Steps);
        Assert.DoesNotContain(obs.Steps, step => step.Contains("--startstreaming"));
        Assert.Equal(1, watchdog.State.Restarts);
        Assert.Equal(ObsWatchdogState.ProcessMissingCode, watchdog.State.LastCause);
        LogEntry warning = Assert.Single(obs.Log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(ObsWatchdogState.ProcessMissingCode, warning.Message);
        Assert.Contains("attempt 1 of 4", warning.Message);

        // The new OBS runs: nothing more to do.
        obs.Ticks(watchdog, 600);
        Assert.Single(obs.Starts);
        Assert.Equal(ObsWatchdogStates.Running, watchdog.State.State);
    }

    [Fact]
    public void TheWatchdog_StartsObsThroughSpectatesLauncher_GateSentinelAndDetachedStart()
    {
        // #409: the watchdog and spectate share ObsLauncher: the gate, the stale crash sentinel
        // cleared before the start, and cmd /c start, so OBS is never the supervisor's child.
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-obs-watchdog-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string stale = Path.Combine(directory, "run_0d9f3c51-0000-0000-0000-000000000000");
            File.WriteAllText(stale, "");
            FakeObs obs = null;
            obs = new FakeObs
            {
                Sentinel = new ObsCrashSentinel(
                    directory,
                    () => obs.Running != null,
                    NullLogger.Instance
                ),
            };
            bool gateHeld = false;
            bool sentinelLeft = true;
            obs.OnStart = () =>
            {
                gateHeld = LaunchGateProbe.HeldElsewhere(obs.GateName);
                sentinelLeft = File.Exists(stale);
            };
            ObsWatchdog watchdog = obs.Watchdog();

            Assert.Equal(ObsWatchdogAction.Start, watchdog.Tick(Start, stopRequested: false));

            Assert.IsType<ObsLauncher>(watchdog.Launcher);
            Assert.Equal(new[] { Launched }, obs.Steps);
            Assert.True(gateHeld);
            Assert.False(sentinelLeft);
            Assert.Contains(
                obs.Log.Entries,
                entry =>
                    entry.Level == LogLevel.Information
                    && entry.Message.Contains("Started OBS detached: obs64 pid 9000")
            );
        }
        finally
        {
            TestTemp.Delete(directory);
        }
    }

    [Fact]
    public void ObsThatKeepsDying_BacksOff1m2m5m_ThenStopsWithOneError()
    {
        var obs = new FakeObs { StartedObsDies = true };
        ObsWatchdog watchdog = obs.Watchdog();

        obs.Ticks(watchdog, 3600);

        // The first start is on the first pass; each next one waits 1, 2, then 5 min after the last.
        Assert.Equal(Start.AddSeconds(1), obs.Starts[0]);
        Assert.Equal(
            new double[] { 0, 60, 180, 480 },
            obs.Starts.Select(at => (at - obs.Starts[0]).TotalSeconds)
        );
        Assert.True(watchdog.State.Exhausted);
        Assert.Equal(ObsWatchdogStates.Exhausted, watchdog.State.State);
        LogEntry error = Assert.Single(obs.Log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(ObsWatchdogState.ExhaustedCode, error.Message);
        Assert.Equal(
            4,
            obs.Log.Entries.Count(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(ObsWatchdogState.ProcessMissingCode)
            )
        );
        Assert.Contains("budget of 4 in 30m exhausted", watchdog.State.Describe(obs.Now));
    }

    [Fact]
    public void AHungObsWithAFrozenStream_IsClosedKilledAndStartedAgain()
    {
        var obs = new FakeObs { Running = new ObsProcessInfo(252, Start.AddHours(-3)) };
        obs.Answer = Status(active: true, bytes: 100);
        ObsWatchdog watchdog = obs.Watchdog();
        Assert.Equal(ObsWatchdogAction.None, watchdog.Tick(Start, false));

        // From now on the websocket does not answer (the 2026-10-09 "Request timed out").
        obs.Answer = null;
        obs.Ticks(watchdog, 179);
        Assert.Empty(obs.Starts);
        Assert.Equal(ObsWatchdogStates.Running, watchdog.State.State);

        obs.Ticks(watchdog, 1);

        Assert.Equal(
            new[] { "close 252", "wait 252 15s", "kill 252", "wait 252 5s", Launched },
            obs.Steps
        );
        Assert.Equal(Start.AddMinutes(3), obs.Starts.Single());
        Assert.Equal(ObsWatchdogState.WebsocketHungCode, watchdog.State.LastCause);
        Assert.Contains(
            obs.Log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(ObsWatchdogState.WebsocketHungCode)
                && entry.Message.Contains("3m (limit 3m)")
        );
        Assert.Equal(1, obs.Saves);
    }

    [Fact]
    public void AHungObsThatClosesWhenAsked_IsNotKilled()
    {
        var obs = new FakeObs
        {
            Running = new ObsProcessInfo(252, Start.AddHours(-1)),
            ClosesWhenAsked = true,
        };
        ObsWatchdog watchdog = obs.Watchdog();

        obs.Ticks(watchdog, 180);

        Assert.Equal(new[] { "close 252", "wait 252 15s", Launched }, obs.Steps.Take(3));
        Assert.DoesNotContain(obs.Steps, step => step.StartsWith("kill"));
        Assert.Single(obs.Starts);
    }

    [Fact]
    public void ASilentWebsocket_WhileSpectateSeesTheStreamLive_NeverKillsObs()
    {
        var obs = new FakeObs
        {
            Running = new ObsProcessInfo(252, Start.AddHours(-1)),
            SpectatorSeesLive = true,
        };
        ObsWatchdog watchdog = obs.Watchdog();

        obs.Ticks(watchdog, 3600);

        Assert.Empty(obs.Steps);
        Assert.Equal(ObsWatchdogStates.Running, watchdog.State.State);
        Assert.Equal("The websocket is silent, but the stream is live.", watchdog.State.Reason);
    }

    [Fact]
    public void Policy_HungButBytesAdvancing_DoesNothing_HungAndFrozen_Restarts()
    {
        var rules = new ObsWatchdogRules { Enabled = true };
        var obs = new ObsProcessInfo(252, Start);
        ObsStreamHealth live = ObsStreamHealth.Next(
            ObsStreamHealth.Next(null, new ObsStreamSample(true, false, 100), Start.AddMinutes(4)),
            new ObsStreamSample(true, false, 900),
            Start.AddMinutes(5)
        );
        ObsStreamHealth frozen = ObsStreamHealth.Next(
            ObsStreamHealth.Next(null, new ObsStreamSample(true, false, 100), Start),
            new ObsStreamSample(true, false, 100),
            Start.AddMinutes(1)
        );
        Assert.True(live.IsLive);
        Assert.Equal(ObsStreamState.Stalled, frozen.State);

        ObsWatchdogObservation Seen(ObsStreamHealth stream) =>
            new()
            {
                Desire = ObsWatchdogDesire.Yes,
                Obs = obs,
                LastAnswerAt = Start,
                Stream = stream,
                WatchingSince = Start,
            };

        Assert.Equal(
            ObsWatchdogAction.None,
            ObsWatchdogPolicy.Decide(Seen(live), new ObsWatchdogState(), Start.AddMinutes(5), rules)
        );
        Assert.Equal(
            ObsWatchdogAction.Restart,
            ObsWatchdogPolicy.Decide(
                Seen(frozen),
                new ObsWatchdogState(),
                Start.AddMinutes(5),
                rules
            )
        );
        Assert.Equal(
            ObsWatchdogAction.Restart,
            ObsWatchdogPolicy.Decide(Seen(null), new ObsWatchdogState(), Start.AddMinutes(5), rules)
        );
        // Inside OBS:HungAfter nothing happens, live or not.
        Assert.Equal(
            ObsWatchdogAction.None,
            ObsWatchdogPolicy.Decide(
                Seen(frozen),
                new ObsWatchdogState(),
                Start.AddMinutes(2),
                rules
            )
        );
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void NotDesired_StopRequested_OrOff_DoesNothing(
        bool enabled,
        bool desired,
        bool stopRequested
    )
    {
        var obs = new FakeObs { Enabled = enabled, Desired = desired };
        ObsWatchdog watchdog = obs.Watchdog();

        obs.Ticks(watchdog, 3600, stopRequested);

        Assert.Empty(obs.Steps);
        Assert.Equal(
            !enabled ? ObsWatchdogStates.Off
                : !desired ? ObsWatchdogStates.NotDesired
                : ObsWatchdogStates.Stopping,
            watchdog.State.State
        );
        Assert.Equal(0, obs.Probes);
    }

    [Fact]
    public void AnObsTheWatchdogStarted_GetsTheFullHungAfterBeforeItCountsAsHung()
    {
        var obs = new FakeObs { NewObsNeverAnswers = true };
        ObsWatchdog watchdog = obs.Watchdog();
        watchdog.Tick(Start, false);
        Assert.Single(obs.Starts);

        obs.Ticks(watchdog, 179);
        Assert.Single(obs.Starts);
        Assert.Equal(ObsWatchdogStates.Waiting, watchdog.State.State);

        // 3 min after its start it is hung: the second start uses the 1 min backoff, already over.
        obs.Ticks(watchdog, 1);
        Assert.Equal(2, obs.Starts.Count);
        Assert.Equal(Start.AddMinutes(3), obs.Starts[1]);
    }

    [Fact]
    public void NoEndpoint_IsNeverAHang_AndSession0_StartsNothing()
    {
        var unprobed = new FakeObs
        {
            Running = new ObsProcessInfo(252, Start.AddHours(-1)),
            Unconfigured = true,
        };
        ObsWatchdog watchdog = unprobed.Watchdog();
        unprobed.Ticks(watchdog, 3600);
        Assert.Empty(unprobed.Steps);

        var service = new FakeObs { CannotControl = "the supervisor runs in session 0." };
        ObsWatchdog suppressed = service.Watchdog();
        service.Ticks(suppressed, 600);
        Assert.Empty(service.Steps);
        Assert.Equal(ObsWatchdogStates.Suppressed, suppressed.State.State);
        Assert.Single(
            service.Log.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("session 0")
        );
    }

    [Fact]
    public void AHungObsThatWillNotDie_IsNotStartedTwice()
    {
        var obs = new FakeObs
        {
            Running = new ObsProcessInfo(252, Start.AddHours(-1)),
            Unkillable = true,
        };
        ObsWatchdog watchdog = obs.Watchdog();

        obs.Ticks(watchdog, 180);

        Assert.Empty(obs.Starts);
        Assert.Equal(1, watchdog.State.Restarts);
        Assert.Contains("no second OBS was started", watchdog.State.LastDetail);
    }

    [Fact]
    public void Describe_IsTheStatusLine()
    {
        var obs = new FakeObs { Running = new ObsProcessInfo(252, Start.AddHours(-1)) };
        obs.Answer = Status(active: false, bytes: null);
        ObsWatchdog watchdog = obs.Watchdog();
        watchdog.Tick(Start, false);

        Assert.Equal(
            "running, pid 252, websocket answered 12s ago, stream Inactive; restarts 0 of 4 in 30m.",
            watchdog.State.Describe(Start.AddSeconds(12))
        );

        // Not watching shows why, not a budget (ASA-SERVER showed "off; restarts 0 of 0 in 0s").
        Assert.Equal("off.", new ObsWatchdogState().Describe(Start));
        var off = new FakeObs { Enabled = false };
        ObsWatchdog idle = off.Watchdog();
        idle.Tick(Start, false);
        Assert.Equal("off. ServiceRestart:ObsWatchdog is false.", idle.State.Describe(Start));
        var undesired = new FakeObs { Desired = false };
        ObsWatchdog unarmed = undesired.Watchdog();
        unarmed.Tick(Start, false);
        Assert.Equal("not_desired. not armed", unarmed.State.Describe(Start));
    }

    [Fact]
    public void Rules_ComeFromServiceRestartAndObsHungAfter()
    {
        ObsWatchdogRules defaults = new ServiceRestartSettings().ObsRules(new OBSSettings());
        Assert.False(defaults.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(3), defaults.HungAfter);
        Assert.Equal(4, defaults.Budget);
        Assert.Equal(TimeSpan.FromMinutes(30), defaults.Window);
        Assert.Equal(
            new double[] { 0, 1, 2, 5, 10, 10 },
            Enumerable.Range(0, 6).Select(used => defaults.DelayFor(used).TotalMinutes)
        );
        Assert.Equal(
            "--profile \"HeroesReplay\" --collection \"HeroesReplay\"",
            defaults.Arguments
        );

        ObsWatchdogRules set = new ServiceRestartSettings
        {
            ObsWatchdog = true,
            ObsBudget = 2,
            ObsBackoff = new List<TimeSpan> { TimeSpan.FromSeconds(30) },
        }.ObsRules(
            new OBSSettings
            {
                HungAfter = TimeSpan.FromMinutes(5),
                ExecutablePath = @"D:\obs\obs64.exe",
                ProfileName = "Live",
                SceneCollectionName = "Show",
            }
        );
        Assert.True(set.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), set.HungAfter);
        Assert.Equal(2, set.Budget);
        Assert.Equal(TimeSpan.FromSeconds(30), set.DelayFor(3));
        Assert.Equal(@"D:\obs\obs64.exe", set.ExecutablePath);
        Assert.Equal("--profile \"Live\" --collection \"Show\"", set.Arguments);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("appsettings.dev.json", false)]
    [InlineData("appsettings.prod.json", true)]
    public void TheWatchdogIsOnInProd_AndOffInDevAndTheBase(string overlay, bool on)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(
            System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        );
        if (overlay != null)
        {
            builder.AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, overlay));
        }

        IConfigurationRoot configuration = builder.Build();
        ServiceRestartSettings restart = configuration
            .GetSection("ServiceRestart")
            .Get<ServiceRestartSettings>();
        OBSSettings obs = configuration.GetSection("OBS").Get<OBSSettings>();

        Assert.Equal(on, restart.ObsWatchdog);
        Assert.Equal(TimeSpan.FromMinutes(3), obs.HungAfter);
        Assert.Equal(4, restart.ObsBudget);
        Assert.Equal(TimeSpan.FromSeconds(30), restart.ObsWatchdogInterval);
    }

    [Fact]
    public void ResolveExecutable_PrefersTheSetting_ThenTheRegistry_ThenProgramFiles()
    {
        string installed = @"D:\Apps\obs-studio";
        string fromRegistry = System.IO.Path.Combine(installed, "bin", "64bit", "obs64.exe");

        Assert.Equal(
            @"E:\obs64.exe",
            ObsLaunchDecision.ResolveExecutable(@"E:\obs64.exe", () => installed, _ => true)
        );
        Assert.Equal(
            fromRegistry,
            ObsLaunchDecision.ResolveExecutable(null, () => installed, path => path == fromRegistry)
        );
        Assert.EndsWith(
            System.IO.Path.Combine("obs-studio", "bin", "64bit", "obs64.exe"),
            ObsLaunchDecision.ResolveExecutable(null, () => installed, _ => false)
        );
        Assert.EndsWith(
            System.IO.Path.Combine("obs-studio", "bin", "64bit", "obs64.exe"),
            ObsLaunchDecision.ResolveExecutable(
                null,
                () => throw new UnauthorizedAccessException(),
                _ => true
            )
        );
    }

    private static JObject Status(bool active, long? bytes)
    {
        var status = new JObject { ["outputActive"] = active, ["outputReconnecting"] = false };
        if (bytes is long value)
        {
            status["outputBytes"] = value;
        }

        return status;
    }

    /// <summary>A fake obs64 on a fake clock: start, close, kill, and the websocket probe.</summary>
    private sealed class FakeObs : IProcessStarter, IProcessTable
    {
        private int nextPid = 9000;

        /// <summary>This test's own launch gate, never the stack's.</summary>
        public string GateName { get; } = LaunchGateProbe.NewName();

        /// <summary>The launcher's crash sentinel. Null clears none.</summary>
        public ObsCrashSentinel Sentinel { get; init; }

        /// <summary>Runs inside the fake <c>cmd /c start</c>, before OBS shows.</summary>
        public Action OnStart { get; set; }

        public DateTimeOffset Now { get; private set; } = Start;
        public bool Enabled { get; init; } = true;
        public bool Desired { get; init; } = true;
        public string CannotControl { get; init; }
        public ObsProcessInfo Running { get; set; }

        /// <summary>GetStreamStatus while OBS answers; null is no answer.</summary>
        public JObject Answer { get; set; }
        public bool Unconfigured { get; init; }
        public bool SpectatorSeesLive { get; init; }
        public bool ClosesWhenAsked { get; init; }
        public bool Unkillable { get; init; }
        public bool StartedObsDies { get; init; }

        /// <summary>An OBS the watchdog starts runs, but its websocket never answers.</summary>
        public bool NewObsNeverAnswers { get; init; }
        public List<string> Steps { get; } = new();
        public List<DateTimeOffset> Starts { get; } = new();
        public int Probes { get; private set; }
        public int Saves { get; private set; }
        public ListLogger Log { get; } = new();

        public ObsWatchdog Watchdog() =>
            new()
            {
                Rules = new ServiceRestartSettings { ObsWatchdog = Enabled }.ObsRules(
                    new OBSSettings { ExecutablePath = Exe }
                ),
                WatchingSince = Start,
                Desired = () => Desired ? ObsWatchdogDesire.Yes : ObsWatchdogDesire.No("not armed"),
                CannotControl = () => CannotControl,
                FindObs = () => Running,
                Probe = () =>
                {
                    Probes++;
                    if (Unconfigured)
                    {
                        return ObsWatchdogProbe.Unconfigured("no endpoint");
                    }

                    return Answer == null
                        ? ObsWatchdogProbe.NoAnswer("Request timed out")
                        : ObsWatchdogProbe.Answered(Answer);
                },
                SpectatorSeesLiveStream = () => SpectatorSeesLive,
                Close = pid =>
                {
                    Steps.Add("close " + pid);
                    if (ClosesWhenAsked)
                    {
                        Running = null;
                    }
                },
                WaitForExit = (pid, wait) =>
                {
                    Steps.Add($"wait {pid} {(int)wait.TotalSeconds}s");
                    return Running?.Pid != pid;
                },
                Kill = pid =>
                {
                    Steps.Add("kill " + pid);
                    if (!Unkillable)
                    {
                        Running = null;
                    }
                },
                // The real shared launcher (#409) on this fake's process table and cmd.
                Launcher = new ObsLauncher(this, this, Sentinel, Log)
                {
                    GateName = GateName,
                    ExecutableExists = _ => true,
                    Now = () => Now,
                    Wait = _ => { },
                },
                Changed = () => Saves++,
                Logger = Log,
            };

        /// <summary>The fake <c>cmd /c start</c>: OBS shows in the table at once, unless it dies.</summary>
        ProcessRun IProcessStarter.Run(ProcessStartInfo start, TimeSpan wait)
        {
            Steps.Add("cmd " + start.Arguments);
            Starts.Add(Now);
            OnStart?.Invoke();
            if (!StartedObsDies)
            {
                Running = new ObsProcessInfo(nextPid++, Now);
                if (NewObsNeverAnswers)
                {
                    Answer = null;
                }
                else
                {
                    Answer ??= Status(active: false, bytes: null);
                }
            }

            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        }

        IReadOnlyList<ProcessTableEntry> IProcessTable.Snapshot() =>
            Running == null
                ? Array.Empty<ProcessTableEntry>()
                :
                [
                    FakeProcessTable.Entry(
                        Running.Pid,
                        FakeProcessStarter.CmdPid,
                        FakeProcessTable.Obs,
                        Running.StartedAt
                    ),
                ];

        ProcessTableEntry IProcessTable.Find(int pid) =>
            ((IProcessTable)this).Snapshot().FirstOrDefault(entry => entry.Pid == pid);

        ProcessKillResult IProcessTable.Kill(ProcessTableEntry entry) =>
            throw new InvalidOperationException("The launcher never kills a process.");

        public void Ticks(ObsWatchdog watchdog, int seconds, bool stopRequested = false)
        {
            for (int pass = 0; pass < seconds; pass++)
            {
                Now += TimeSpan.FromSeconds(1);
                watchdog.Tick(Now, stopRequested);
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
