using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Recording;

/// <summary>
/// #318: after every role exited, services stop stops the recording spectate claimed and left
/// running, and only that recording. It never sends StopStream.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class OrphanRecordingTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 8, 12, 33, 26, TimeSpan.Zero);

    /// <summary>When the claimant spectate process started, some time before its recording.</summary>
    private static readonly DateTimeOffset ClaimantStarted = Started.AddMinutes(-20);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-orphan-" + Guid.NewGuid().ToString("n")
    );

    private readonly RecordingClaimStore claims;

    public OrphanRecordingTests()
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

    [Fact]
    public void NoClaim_DoesNotOpenObs()
    {
        FakeObs obs = Obs();

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5));

        Assert.Equal(OrphanRecordingState.None, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.Equal(0, obs.Opened);
        Assert.Equal("none left by spectate.", check.Describe());
    }

    /// <summary>The run in the issue: spectate exited, OBS kept recording replay 65820711.</summary>
    [Fact]
    public void ClaimedRecordingStillRunning_IsStoppedAndTheStreamIsNotTouched()
    {
        Claim();
        string file = @"C:\heroesreplay\Data\Contexts\65820711\2026-10-08 12-33-26.mp4";
        FakeObs obs = Obs(recordedFor: TimeSpan.FromSeconds(150));
        obs.RecordPath = file;
        var waits = new List<TimeSpan>();

        OrphanRecordingCheck check = Stop(obs, now: Started.AddSeconds(152), wait: waits.Add);

        Assert.Equal(OrphanRecordingState.Stopped, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.Equal(file, check.OutputPath);
        Assert.Contains("replay 65820711", check.Detail, StringComparison.Ordinal);
        Assert.Contains("stream was not touched", check.Detail, StringComparison.Ordinal);
        Assert.StartsWith("STOPPED", check.Describe(), StringComparison.Ordinal);
        Assert.Equal(new[] { "GetRecordStatus", "StopRecord", "GetRecordStatus" }, obs.Requests);
        Assert.DoesNotContain("StopStream", obs.Requests);
        Assert.Empty(waits);
        Assert.False(obs.Recording);
        Assert.Equal(1, obs.Disposed);
        Assert.Null(claims.TryLoad());
    }

    [Fact]
    public void ObsNotRecording_DeletesTheClaimWithoutAStop()
    {
        Claim();
        FakeObs obs = Obs();
        obs.Recording = false;

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5));

        Assert.Equal(OrphanRecordingState.Inactive, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.Equal(new[] { "GetRecordStatus" }, obs.Requests);
        Assert.Null(claims.TryLoad());
    }

    /// <summary>Someone started a recording after spectate's had ended. It is not spectate's.</summary>
    [Fact]
    public void YoungerRecording_IsNotSpectatesAndKeepsRunning()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(3));

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(30));

        Assert.Equal(OrphanRecordingState.NotOwned, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.DoesNotContain("StopRecord", obs.Requests);
        Assert.DoesNotContain("StopStream", obs.Requests);
        Assert.True(obs.Recording);
        Assert.Null(claims.TryLoad());
    }

    [Fact]
    public void ObsNotRunning_DeletesTheClaimWithoutOpeningObs()
    {
        Claim();
        FakeObs obs = Obs();

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5), obsRunning: false);

        Assert.Equal(OrphanRecordingState.ObsNotRunning, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.Equal(0, obs.Opened);
        Assert.Null(claims.TryLoad());
    }

    /// <summary>A spectate outside services.json still runs and owns its recording.</summary>
    [Fact]
    public void ClaimantStillRunning_LeavesItsRecordingAlone()
    {
        Claim(processId: 4242);
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(5));
        int asked = 0;

        OrphanRecordingCheck check = OrphanRecording.Stop(
            claims,
            obsRunning: true,
            pid =>
            {
                asked = pid;
                return Process(pid, ClaimantStarted);
            },
            obs.OpenRecordStop,
            Started.AddMinutes(5)
        );

        Assert.Equal(OrphanRecordingState.ClaimantRunning, check.State);
        Assert.Equal(4242, asked);
        Assert.Equal(0, obs.Opened);
        Assert.NotNull(claims.TryLoad());
    }

    [Fact]
    public void UnreachableWebsocket_KeepsTheClaimAndFailsTheStop()
    {
        Claim();

        OrphanRecordingCheck check = OrphanRecording.Stop(
            claims,
            obsRunning: true,
            _ => null,
            () =>
                throw new ObsUnavailableException(
                    ObsUnavailableException.Unreachable,
                    "OBS at ws://127.0.0.1:4455 did not answer within 3 s."
                ),
            Started.AddMinutes(5)
        );

        Assert.Equal(OrphanRecordingState.Unreachable, check.State);
        Assert.False(check.ConfirmsStopped);
        Assert.Contains("Stop the recording in OBS", check.Detail, StringComparison.Ordinal);
        Assert.NotNull(claims.TryLoad());
    }

    [Fact]
    public void RefusedStop_KeepsTheClaimAndFailsTheStop()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(5));
        obs.StopRecordError = new ObsRequestException("StopRecord", 702, "Output failed.");

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5));

        Assert.Equal(OrphanRecordingState.Failed, check.State);
        Assert.False(check.ConfirmsStopped);
        Assert.NotNull(claims.TryLoad());
    }

    [Fact]
    public void RecordingThatEndedBeforeTheStop_CountsAsEnded()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(5));
        obs.StopRecordError = new ObsRequestException("StopRecord", 501, "Not running.");

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5));

        Assert.Equal(OrphanRecordingState.Inactive, check.State);
        Assert.True(check.ConfirmsStopped);
        Assert.Null(claims.TryLoad());
    }

    [Fact]
    public void RecordingStillActiveAfterTheStop_FailsAfterTheConfirmWindow()
    {
        Claim();
        FakeObs obs = Obs(recordedFor: TimeSpan.FromMinutes(5));
        obs.KeepRecordingOnStop = true;
        var waits = new List<TimeSpan>();

        OrphanRecordingCheck check = Stop(obs, now: Started.AddMinutes(5), wait: waits.Add);

        Assert.Equal(OrphanRecordingState.Failed, check.State);
        Assert.False(check.ConfirmsStopped);
        Assert.NotEmpty(waits);
        Assert.DoesNotContain("StopStream", obs.Requests);
        Assert.NotNull(claims.TryLoad());
    }

    /// <summary>#342: the claimant is its pid and its start time, so a reused pid is not it.</summary>
    [Theory]
    // The claimant itself, read a moment apart.
    [InlineData(true, 0, "heroesreplay.exe", true)]
    [InlineData(true, 1, "heroesreplay.exe", true)]
    // The pid now belongs to a process that started after the claimant, even another heroesreplay.
    [InlineData(true, 1500, "heroesreplay.exe", false)]
    [InlineData(true, 1500, "notepad.exe", false)]
    // A claim from before #342 has no start time: a pid alive since before the claim is the claimant.
    [InlineData(false, 0, "heroesreplay.exe", true)]
    [InlineData(false, 1500, "heroesreplay.exe", false)]
    public void ClaimantRunning_MatchesThePidAndItsStartTime(
        bool claimRecordsStartTime,
        int liveStartedAfterClaimantSeconds,
        string liveName,
        bool running
    )
    {
        var claim = new RecordingClaim
        {
            ReplayId = 65820711,
            StartedAt = Started,
            ProcessId = 4242,
            ProcessStartedAt = claimRecordsStartTime ? ClaimantStarted : null,
        };

        Assert.Equal(
            running,
            OrphanRecording.ClaimantRunning(
                claim,
                pid =>
                    pid == 4242
                        ? Process(
                            pid,
                            ClaimantStarted.AddSeconds(liveStartedAfterClaimantSeconds),
                            liveName
                        )
                        : null
            )
        );
    }

    [Fact]
    public void ClaimantRunning_DeadPid_IsNotRunning()
    {
        var claim = new RecordingClaim
        {
            StartedAt = Started,
            ProcessId = 4242,
            ProcessStartedAt = ClaimantStarted,
        };

        Assert.False(OrphanRecording.ClaimantRunning(claim, _ => null));
        Assert.False(
            OrphanRecording.ClaimantRunning(
                claim with
                {
                    ProcessId = 0,
                },
                pid => Process(pid, ClaimantStarted)
            )
        );
    }

    /// <summary>A pid this process cannot open has no start time: only a heroesreplay counts.</summary>
    [Theory]
    [InlineData("heroesreplay.exe", true)]
    [InlineData("svchost.exe", false)]
    public void ClaimantRunning_UnreadableStartTime_FallsBackToTheName(string name, bool running)
    {
        var claim = new RecordingClaim
        {
            StartedAt = Started,
            ProcessId = 4242,
            ProcessStartedAt = ClaimantStarted,
        };

        Assert.Equal(
            running,
            OrphanRecording.ClaimantRunning(claim, pid => Process(pid, null, name))
        );
    }

    [Theory]
    [InlineData(150, 152, true)]
    [InlineData(1800, 1810, true)]
    // A tenth of the claim's age allows for frames OBS dropped on a long recording.
    [InlineData(3300, 3600, true)]
    [InlineData(180, 1800, false)]
    [InlineData(1, 300, false)]
    public void IsClaimed_MatchesTheClaimsAge(
        int recordedSeconds,
        int claimAgeSeconds,
        bool claimed
    )
    {
        var claim = new RecordingClaim { ReplayId = 65820711, StartedAt = Started };

        Assert.Equal(
            claimed,
            OrphanRecording.IsClaimed(
                claim,
                TimeSpan.FromSeconds(recordedSeconds),
                Started.AddSeconds(claimAgeSeconds)
            )
        );
    }

    private void Claim(int processId = 999999) =>
        claims.Save(
            new RecordingClaim
            {
                ReplayId = 65820711,
                StartedAt = Started,
                ProcessId = processId,
                ProcessStartedAt = ClaimantStarted,
            }
        );

    private static ProcessTableEntry Process(
        int pid,
        DateTimeOffset? startTime,
        string name = "heroesreplay.exe"
    ) => new(pid, 1, name, @"C:\heroesreplay\app\heroesreplay.exe", startTime);

    private static FakeObs Obs(TimeSpan? recordedFor = null)
    {
        FakeObs obs = FakeObs.Packaged();
        obs.RecordedMilliseconds = (long)(recordedFor ?? TimeSpan.FromMinutes(5)).TotalMilliseconds;
        return obs;
    }

    private OrphanRecordingCheck Stop(
        FakeObs obs,
        DateTimeOffset now,
        bool obsRunning = true,
        Action<TimeSpan> wait = null
    ) => OrphanRecording.Stop(claims, obsRunning, _ => null, obs.OpenRecordStop, now, wait);
}
