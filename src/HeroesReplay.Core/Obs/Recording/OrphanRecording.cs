using System;
using System.Globalization;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Recording;

public enum OrphanRecordingState
{
    /// <summary>No claim: spectate left no recording it had not seen finalized.</summary>
    None,

    /// <summary>The spectate process that claimed the recording still runs. It was left alone.</summary>
    ClaimantRunning,

    /// <summary>OBS is not running, so nothing records.</summary>
    ObsNotRunning,

    /// <summary>OBS reported no active recording.</summary>
    Inactive,

    /// <summary>The recording spectate started was still running and was stopped.</summary>
    Stopped,

    /// <summary>OBS records, but not the recording spectate started. It was left running.</summary>
    NotOwned,

    /// <summary>OBS is running, but its websocket did not answer. The recording may still grow.</summary>
    Unreachable,

    /// <summary>OBS refused the stop, or still reports the recording active after it.</summary>
    Failed,

    /// <summary>OBS did not report its recording state.</summary>
    Unknown,
}

/// <summary>
/// What <c>services stop</c>, or the next spectate when it starts, found and did about a recording
/// spectate left running.
/// </summary>
public sealed record OrphanRecordingCheck(
    OrphanRecordingState State,
    string Detail,
    string OutputPath = null
)
{
    /// <summary>False only when a recording spectate started may still be running.</summary>
    public bool ConfirmsStopped =>
        State
            is not OrphanRecordingState.Unreachable
                and not OrphanRecordingState.Failed
                and not OrphanRecordingState.Unknown;

    public string Describe()
    {
        string state = State switch
        {
            OrphanRecordingState.None => "none left by spectate",
            OrphanRecordingState.ClaimantRunning => "left to the spectate process that owns it",
            OrphanRecordingState.ObsNotRunning => "OBS is not running",
            OrphanRecordingState.Inactive => "not recording",
            OrphanRecordingState.Stopped => "STOPPED the recording spectate left running",
            OrphanRecordingState.NotOwned =>
                "recording, but not spectate's recording; left running",
            OrphanRecordingState.Unreachable =>
                "running, but its websocket did not answer, so the recording spectate started may still be running",
            OrphanRecordingState.Failed => "NOT STOPPED",
            _ => "not confirmed",
        };
        return string.IsNullOrWhiteSpace(Detail) ? state + "." : $"{state}. {Detail}";
    }
}

/// <summary>
/// Stops the OBS recording that spectate started and left running: spectate was killed after its
/// graceful budget, or its own stop did not finish (#318), or the supervisor restarted it after a
/// crash, a stale heartbeat, or a stalled launch (#342). <c>services stop</c> runs it after every
/// role exited, and spectate runs it when it starts, before its first replay
/// (<see cref="OrphanRecordingOnStart"/>). It acts only on the claim spectate wrote
/// (<see cref="RecordingClaimStore"/>), only when the claimant is dead, and only when the recording
/// OBS reports started when the claim says. It sends <c>StopRecord</c> and never touches the stream.
/// </summary>
public static class OrphanRecording
{
    /// <summary>The smallest difference allowed between the claim's age and OBS's recorded time.</summary>
    public static readonly TimeSpan MatchTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The allowance between the claimant's recorded start time and the live pid's, as for a
    /// role's or the supervisor's recorded start time.
    /// </summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>How long OBS gets to report the recording inactive after <c>StopRecord</c>.</summary>
    public static readonly TimeSpan StopConfirmTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan StopPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>obs-websocket RequestStatus 501: the output is not running.</summary>
    private const int OutputNotRunning = 501;

    /// <summary>
    /// True when a recording OBS has recorded for <paramref name="recordedFor"/> is the claimed
    /// one: it started within the tolerance of the claim (2 minutes, or a tenth of the claim's age
    /// when that is longer, for frames OBS dropped). A recording someone started after spectate's
    /// ended is younger than the claim and does not match.
    /// </summary>
    public static bool IsClaimed(RecordingClaim claim, TimeSpan recordedFor, DateTimeOffset now)
    {
        if (claim == null)
        {
            return false;
        }

        TimeSpan sinceClaim = now - claim.StartedAt;
        TimeSpan tolerance = sinceClaim / 10 > MatchTolerance ? sinceClaim / 10 : MatchTolerance;
        return (recordedFor - sinceClaim).Duration() <= tolerance;
    }

    /// <summary>
    /// True while the spectate that wrote <paramref name="claim"/> still runs, and so still owns
    /// its recording: its pid is alive and started when the claim says (within 2 s), so a reused
    /// pid does not count (#342). A claim from a build that recorded no process start time counts
    /// a live pid that started no later than the claim, since a reused pid starts after the
    /// claimant exited. When the pid's start time cannot be read from this process, a running
    /// <c>heroesreplay</c> with that pid counts.
    /// </summary>
    public static bool ClaimantRunning(
        RecordingClaim claim,
        Func<int, ProcessTableEntry> findProcess
    )
    {
        if (claim == null || claim.ProcessId <= 0 || findProcess == null)
        {
            return false;
        }

        ProcessTableEntry process = findProcess(claim.ProcessId);
        if (process == null)
        {
            return false;
        }

        if (process.StartTime is not DateTimeOffset started)
        {
            return ServiceProcessPlan.IsHeroesReplay(process.Name);
        }

        return claim.ProcessStartedAt is DateTimeOffset recorded
            ? (started - recorded).Duration() <= StartTimeTolerance
            : started <= claim.StartedAt;
    }

    /// <summary>
    /// Never throws. The claim is deleted once OBS answered for it (no recording, another
    /// recording, or the claimed one stopped), and kept while that is not known.
    /// <paramref name="findProcess"/> looks up the claim's pid (<see cref="ProcessTable.Find"/>);
    /// a claimant that still runs owns its recording (<see cref="ClaimantRunning"/>).
    /// <paramref name="open"/> is called only when a claim exists, its claimant is dead, and OBS
    /// runs.
    /// </summary>
    public static OrphanRecordingCheck Stop(
        RecordingClaimStore claims,
        bool obsRunning,
        Func<int, ProcessTableEntry> findProcess,
        Func<IObsRecordStopSession> open,
        DateTimeOffset now,
        Action<TimeSpan> wait = null
    )
    {
        RecordingClaim claim;
        try
        {
            claim = claims?.TryLoad();
        }
        catch (Exception e)
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.Unknown,
                "The recording claim could not be read. " + e.Message
            );
        }

        if (claim == null)
        {
            return new OrphanRecordingCheck(OrphanRecordingState.None, null);
        }

        string claimed = Describe(claim);
        if (ClaimantRunning(claim, findProcess))
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.ClaimantRunning,
                $"Spectate pid {claim.ProcessId} still runs and owns {claimed}."
            );
        }

        if (!obsRunning)
        {
            Clear(claims);
            return new OrphanRecordingCheck(
                OrphanRecordingState.ObsNotRunning,
                $"Nothing records {claimed}."
            );
        }

        try
        {
            using IObsRecordStopSession session = open();
            Status status = Read(session);
            if (status.Active == null)
            {
                return new OrphanRecordingCheck(
                    OrphanRecordingState.Unknown,
                    $"GetRecordStatus did not say whether OBS records {claimed}."
                );
            }

            if (status.Active == false)
            {
                Clear(claims);
                return new OrphanRecordingCheck(
                    OrphanRecordingState.Inactive,
                    $"OBS reported no active recording; {claimed} ended."
                );
            }

            if (status.RecordedFor is not TimeSpan recordedFor)
            {
                return new OrphanRecordingCheck(
                    OrphanRecordingState.Unknown,
                    $"OBS records, but GetRecordStatus gave no duration, so it was not matched to {claimed}. It was not stopped."
                );
            }

            if (!IsClaimed(claim, recordedFor, now))
            {
                Clear(claims);
                return new OrphanRecordingCheck(
                    OrphanRecordingState.NotOwned,
                    $"OBS has recorded for {Format(recordedFor)}, which does not match {claimed} ({Format(now - claim.StartedAt)} ago). That recording was not stopped."
                );
            }

            return StopClaimed(session, claims, claim, recordedFor, wait);
        }
        catch (ObsUnavailableException e)
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.Unreachable,
                $"{e.Message} It was not checked for {claimed}. Stop the recording in OBS."
            );
        }
        catch (Exception e)
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.Unknown,
                $"{e.Message} It was not checked for {claimed}."
            );
        }
    }

    private static OrphanRecordingCheck StopClaimed(
        IObsRecordStopSession session,
        RecordingClaimStore claims,
        RecordingClaim claim,
        TimeSpan recordedFor,
        Action<TimeSpan> wait
    )
    {
        string claimed = Describe(claim);
        string path;
        try
        {
            path = session.StopRecord();
        }
        catch (ObsRequestException e) when (e.Status == OutputNotRunning)
        {
            Clear(claims);
            return new OrphanRecordingCheck(
                OrphanRecordingState.Inactive,
                $"The recording ended before StopRecord; {claimed} ended."
            );
        }
        catch (ObsRequestException e)
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.Failed,
                $"OBS refused StopRecord for {claimed}. {e.Message} Stop the recording in OBS."
            );
        }

        try
        {
            TimeSpan waited = TimeSpan.Zero;
            while (Read(session).Active == true)
            {
                if (waited >= StopConfirmTimeout)
                {
                    return new OrphanRecordingCheck(
                        OrphanRecordingState.Failed,
                        $"OBS still reports {claimed} active {Format(StopConfirmTimeout)} after StopRecord. Stop the recording in OBS.",
                        path
                    );
                }

                wait?.Invoke(StopPoll);
                waited += StopPoll;
            }
        }
        catch (Exception e)
        {
            return new OrphanRecordingCheck(
                OrphanRecordingState.Failed,
                $"StopRecord was sent for {claimed}, but OBS did not confirm it stopped. {e.Message} Check the recording in OBS.",
                path
            );
        }

        Clear(claims);
        return new OrphanRecordingCheck(
            OrphanRecordingState.Stopped,
            $"Stopped {claimed} after {Format(recordedFor)} of recording"
                + (string.IsNullOrWhiteSpace(path) ? "." : $"; the file is {path}.")
                + " The OBS stream was not touched.",
            path
        );
    }

    private static Status Read(IObsRecordStopSession session)
    {
        JObject status = session.Get("GetRecordStatus");
        long? duration = ObsResponse.Long(status, "outputDuration");
        return new Status(
            ObsResponse.Bool(status, "outputActive"),
            duration is long milliseconds && milliseconds >= 0
                ? TimeSpan.FromMilliseconds(milliseconds)
                : null
        );
    }

    private static void Clear(RecordingClaimStore claims)
    {
        try
        {
            claims?.Clear();
        }
        catch (Exception)
        {
            // The claim is checked against OBS again on the next stop; a stale one matches nothing.
        }
    }

    private static string Describe(RecordingClaim claim) =>
        "the recording spectate pid "
        + claim.ProcessId
        + " started for replay "
        + (claim.ReplayId?.ToString(CultureInfo.InvariantCulture) ?? "(no id)")
        + " at "
        + claim.StartedAt.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm:ss'Z'",
            CultureInfo.InvariantCulture
        );

    private static string Format(TimeSpan value) =>
        value.ToString(
            value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss",
            CultureInfo.InvariantCulture
        );

    private readonly record struct Status(bool? Active, TimeSpan? RecordedFor);
}
