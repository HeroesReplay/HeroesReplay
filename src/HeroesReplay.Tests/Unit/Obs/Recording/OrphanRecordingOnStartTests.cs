using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

/// <summary>
/// #342: the supervisor restarted spectate after a crash, a stale heartbeat, or a stalled launch,
/// so services stop never ran. Before its first replay the new spectate stops the recording the
/// dead one claimed and left running, with StopRecord only. Anything else it leaves alone.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class OrphanRecordingOnStartTests : IDisposable
{
    private const int DeadSpectate = 7310;

    private static readonly DateTimeOffset Started = new(2026, 10, 8, 12, 33, 26, TimeSpan.Zero);
    private static readonly DateTimeOffset ClaimantStarted = Started.AddMinutes(-20);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-orphan-start-" + Guid.NewGuid().ToString("n")
    );

    private readonly RecordingClaimStore claims;
    private readonly ListLogger logger = new();

    public OrphanRecordingOnStartTests()
    {
        Directory.CreateDirectory(directory);
        claims = new RecordingClaimStore(Path.Combine(directory, "obs-recording.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The issue: spectate died 12 minutes into replay 65820711 and OBS kept recording.</summary>
    [Fact]
    public void DeadClaimant_MatchingRecording_IsStoppedWithoutTheStream()
    {
        Claim();
        string file = @"C:\heroesreplay\Data\Contexts\65820711\2026-10-08 12-33-26.mp4";
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(12));
        obs.RecordPath = file;

        OrphanRecordingCheck check = Run(obs, now: Started.AddMinutes(12), findProcess: _ => null);

        Assert.Equal(OrphanRecordingState.Stopped, check.State);
        Assert.Equal(file, check.OutputPath);
        Assert.Equal(new[] { "GetRecordStatus", "StopRecord", "GetRecordStatus" }, obs.Requests);
        Assert.DoesNotContain("StopStream", obs.Requests);
        Assert.False(obs.Recording);
        Assert.Equal(1, obs.Opened);
        Assert.Equal(1, obs.Disposed);
        Assert.Null(claims.TryLoad());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("STOPPED", message, StringComparison.Ordinal);
        Assert.Contains($"pid {DeadSpectate}", message, StringComparison.Ordinal);
        Assert.Contains("replay 65820711", message, StringComparison.Ordinal);
        Assert.Contains(file, message, StringComparison.Ordinal);
        Assert.Contains("stream was not touched", message, StringComparison.Ordinal);
    }

    /// <summary>Windows gave the dead spectate's pid to another process. That is not the claimant.</summary>
    [Fact]
    public void ReusedClaimantPid_CountsAsDead_AndTheRecordingIsStopped()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(12));

        OrphanRecordingCheck check = Run(
            obs,
            now: Started.AddMinutes(12),
            findProcess: pid => Process(pid, Started.AddMinutes(11))
        );

        Assert.Equal(OrphanRecordingState.Stopped, check.State);
        Assert.Contains("StopRecord", obs.Requests);
        Assert.Null(claims.TryLoad());
    }

    /// <summary>The claimant still runs (two spectates at once): its recording is its own.</summary>
    [Fact]
    public void LiveClaimant_IsLeftAloneAndLoggedOnce()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(12));
        var asked = new List<int>();

        OrphanRecordingCheck check = Run(
            obs,
            now: Started.AddMinutes(12),
            findProcess: pid =>
            {
                asked.Add(pid);
                return Process(pid, ClaimantStarted);
            }
        );

        Assert.Equal(OrphanRecordingState.ClaimantRunning, check.State);
        Assert.Equal(new[] { DeadSpectate }, asked);
        Assert.Equal(0, obs.Opened);
        Assert.Empty(obs.Requests);
        Assert.True(obs.Recording);
        Assert.NotNull(claims.TryLoad());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains($"pid {DeadSpectate} still runs", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dead spectate's recording ended, and someone started another later. It is younger than
    /// the claim, so it is not the claimed one, and it keeps running.
    /// </summary>
    [Fact]
    public void RecordingYoungerThanTheClaim_IsLeftAloneAndLoggedOnce()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(3));

        OrphanRecordingCheck check = Run(obs, now: Started.AddMinutes(30), findProcess: _ => null);

        Assert.Equal(OrphanRecordingState.NotOwned, check.State);
        Assert.Equal(new[] { "GetRecordStatus" }, obs.Requests);
        Assert.True(obs.Recording);
        Assert.Null(claims.TryLoad());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains(
            "not spectate's recording; left running",
            message,
            StringComparison.Ordinal
        );
    }

    /// <summary>OBS runs, but its websocket does not answer: nothing is sent and the claim stays.</summary>
    [Fact]
    public void ObsUnreachable_IsLeftAloneAndLoggedOnce()
    {
        Claim();
        int opened = 0;
        OrphanRecordingOnStart start = Start(
            obsRunning: true,
            findProcess: _ => null,
            open: () =>
            {
                opened++;
                throw new ObsUnavailableException(
                    ObsUnavailableException.Unreachable,
                    "OBS at ws://127.0.0.1:4455 did not answer within 3 s."
                );
            },
            now: Started.AddMinutes(12)
        );

        OrphanRecordingCheck check = start.Run();

        Assert.Equal(OrphanRecordingState.Unreachable, check.State);
        Assert.Equal(1, opened);
        Assert.NotNull(claims.TryLoad());
        (LogLevel level, string message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("did not answer", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ObsNotRunning_DeletesTheClaimWithoutOpeningObs()
    {
        Claim();
        FakeObs obs = Obs();

        OrphanRecordingCheck check = Run(
            obs,
            now: Started.AddMinutes(12),
            findProcess: _ => null,
            obsRunning: false
        );

        Assert.Equal(OrphanRecordingState.ObsNotRunning, check.State);
        Assert.Equal(0, obs.Opened);
        Assert.Null(claims.TryLoad());
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public void NoClaim_DoesNotOpenObs()
    {
        FakeObs obs = Obs();

        OrphanRecordingCheck check = Run(obs, now: Started, findProcess: _ => null);

        Assert.Equal(OrphanRecordingState.None, check.State);
        Assert.Equal(0, obs.Opened);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level > LogLevel.Debug);
    }

    /// <summary>OBS:Enabled=false: spectate sends OBS nothing, so the claim is left to services stop.</summary>
    [Fact]
    public void ObsDisabled_SendsNothingAndKeepsTheClaim()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(12));
        OBSSettings settings = FakeObs.Settings();
        settings.Enabled = false;

        OrphanRecordingCheck check = Start(
                obsRunning: true,
                findProcess: _ => null,
                open: obs.OpenRecordStop,
                now: Started.AddMinutes(12),
                settings: settings
            )
            .Run();

        Assert.Null(check);
        Assert.Equal(0, obs.Opened);
        Assert.True(obs.Recording);
        Assert.NotNull(claims.TryLoad());
        Assert.DoesNotContain(logger.Entries, entry => entry.Level > LogLevel.Debug);
    }

    private void Claim() =>
        claims.Save(
            new RecordingClaim
            {
                ReplayId = 65820711,
                StartedAt = Started,
                ProcessId = DeadSpectate,
                ProcessStartedAt = ClaimantStarted,
            }
        );

    private static FakeObs Obs(TimeSpan? recordedFor = null)
    {
        FakeObs obs = FakeObs.Packaged();
        obs.RecordedMilliseconds = (long)(recordedFor ?? TimeSpan.FromMinutes(5)).TotalMilliseconds;
        return obs;
    }

    private static ProcessTableEntry Process(int pid, DateTimeOffset startTime) =>
        new(pid, 1, "heroesreplay.exe", @"C:\heroesreplay\app\heroesreplay.exe", startTime);

    private OrphanRecordingCheck Run(
        FakeObs obs,
        DateTimeOffset now,
        Func<int, ProcessTableEntry> findProcess,
        bool obsRunning = true
    ) => Start(obsRunning, findProcess, obs.OpenRecordStop, now).Run();

    private OrphanRecordingOnStart Start(
        bool obsRunning,
        Func<int, ProcessTableEntry> findProcess,
        Func<IObsRecordStopSession> open,
        DateTimeOffset now,
        OBSSettings settings = null
    )
    {
        if (settings == null)
        {
            settings = FakeObs.Settings();
            settings.Enabled = true;
        }

        return new OrphanRecordingOnStart(
            settings,
            logger,
            claims,
            () => obsRunning,
            findProcess,
            open,
            new FixedClock(now),
            _ => { }
        );
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
