using System;
using System.IO;
using System.Threading;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// Owns at most the recording this process started. Request exceptions leave the retry
/// delegate. Timeouts, disconnects, and split files are failed results.
/// </summary>
internal sealed class RecordingSession
{
    private readonly ILogger logger;
    private readonly IObsRecordSocket socket;
    private readonly ObsRecordingBudget budget;
    private readonly RecordingClaimStore claims;
    private readonly Func<DateTimeOffset> now;
    private readonly object gate = new();
    private readonly ManualResetEventSlim wake = new(false);
    private volatile bool ownsRecording;
    private bool acceptSplit;
    private bool activeSeen;
    private bool stoppedSeen;
    private bool splitSeen;
    private bool disconnectSeen;
    private string stoppedPath;

    // The StartStream in flight (#407): when it was sent, whether OBS reported it starting, and
    // whether OBS then reported the output stopped (the start failed).
    private DateTimeOffset? streamStartedAt;
    private bool streamStarting;
    private bool streamStoppedSeen;

    /// <summary>
    /// <paramref name="claims"/> is where the recording this process starts is claimed for
    /// <c>services stop</c> (#318) and the next spectate's start (#342). Null writes no claim.
    /// </summary>
    public RecordingSession(
        ILogger logger,
        IObsRecordSocket socket,
        ObsRecordingBudget budget,
        RecordingClaimStore claims = null,
        Func<DateTimeOffset> now = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.budget = budget ?? ObsRecordingBudget.Default;
        this.claims = claims;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        if (this.budget.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        socket.RecordSignal += OnSignal;
    }

    /// <param name="verifySelection">
    /// Runs once the websocket is identified and before OBS is asked to stop or start a
    /// recording. A failed result returns <see cref="ObsOutputFailure.SelectionMismatch"/>.
    /// </param>
    public ObsRecordingResult StartRecording(
        Func<bool> shouldRecord,
        Action ensureConnected,
        int? replayId,
        string reason,
        Action prepareOutput = null,
        Func<ObsSelectionResult> verifySelection = null
    )
    {
        if (shouldRecord != null && !shouldRecord())
        {
            return ObsRecordingResult.Failed(
                ObsOutputFailure.NotRequested,
                "Recording is not enabled for this replay."
            );
        }

        if (!ownsRecording)
        {
            ClearFlags();
        }

        var attempt = new Attempt();
        ObsRecordingResult result = Execute(
            () =>
                StartCore(
                    attempt,
                    ensureConnected,
                    replayId,
                    reason,
                    prepareOutput,
                    verifySelection
                ),
            error => ObsRecordingResult.Failed(ObsOutputFailure.RequestError, error.Message),
            "start OBS recording"
        );
        LogFailure(result, replayId, "start");
        return result;
    }

    public ObsRecordingResult StopRecording(Action ensureConnected, int? replayId)
    {
        if (!ownsRecording)
        {
            return ObsRecordingResult.Failed(
                ObsOutputFailure.NotOwned,
                "This process does not own the OBS recording."
            );
        }

        ObsRecordingResult result = Execute(
            () => StopCore(ensureConnected, replayId),
            error =>
            {
                // A thrown stop did not finalize a path. Keeping ownership would make the
                // next start look successful without a new file.
                ReleaseOwnership();
                return ObsRecordingResult.Failed(ObsOutputFailure.RequestError, error.Message);
            },
            "stop OBS recording"
        );
        LogFailure(result, replayId, "stop");
        return result;
    }

    /// <summary>
    /// One StartStream, and a wait until it ends (#407): OBS reports the output active, reports it
    /// stopped after it began (the start failed), the websocket drops, or
    /// <see cref="ObsRecordingBudget.StreamStartTimeout"/> passes. A request that throws before
    /// StartStream goes out is retried; once it went out it never is, because a StartStream that
    /// timed out may still be running in OBS. A StartStream that has not ended is not followed
    /// by another until OBS reports it ended, the websocket drops, or
    /// <see cref="StreamStartPendingLimit"/> passes.
    /// </summary>
    public ObsStreamResult StartStreaming(Action ensureConnected)
    {
        bool sent = false;
        return Execute(
            () => StartStreamCore(ensureConnected, () => sent = true),
            error =>
                error is TimeoutException
                    ? ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, error.Message)
                    : ObsStreamResult.Failed(ObsOutputFailure.RequestError, error.Message),
            "start OBS streaming",
            retry: () => !sent
        );
    }

    /// <summary>How long a StartStream that never ended blocks the next one.</summary>
    public static readonly TimeSpan StreamStartPendingLimit = TimeSpan.FromMinutes(5);

    public ObsStreamResult StopStreaming(Action ensureConnected)
    {
        return Execute(
            () => StopStreamCore(ensureConnected),
            error =>
                error is TimeoutException
                    ? ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, error.Message)
                    : ObsStreamResult.Failed(ObsOutputFailure.RequestError, error.Message),
            "stop OBS streaming"
        );
    }

    private ObsStreamResult StartStreamCore(Action ensureConnected, Action sending)
    {
        EnsureIdentified(ensureConnected);
        if (!socket.IsConnected)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected."
            );
        }

        if (socket.IsStreamActive())
        {
            EndStreamStart();
            return ObsStreamResult.ConfirmedActive();
        }

        DateTimeOffset? pending = PendingStreamStart();
        if (pending is DateTimeOffset sentAt)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "The StartStream sent at "
                    + sentAt.ToUniversalTime().ToString("HH:mm:ss")
                    + "Z has not ended (OBS reported it neither started nor stopped), so no second StartStream was sent."
            );
        }

        lock (gate)
        {
            streamStartedAt = now();
            streamStarting = false;
            streamStoppedSeen = false;
            wake.Reset();
        }

        logger.LogInformation("Starting OBS stream.");
        sending();
        socket.StartStream();
        if (socket.IsStreamActive())
        {
            EndStreamStart();
            return ObsStreamResult.ConfirmedActive();
        }

        ObsOutputFailure waited = WaitUntil(ProbeStreamStart, budget.StreamStartTimeout);
        if (waited == ObsOutputFailure.None && socket.IsStreamActive())
        {
            EndStreamStart();
            return ObsStreamResult.ConfirmedActive();
        }

        if (waited == ObsOutputFailure.Disconnected)
        {
            EndStreamStart();
            return ObsStreamResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before the stream was confirmed."
            );
        }

        if (waited == ObsOutputFailure.NotConfirmed)
        {
            EndStreamStart();
            return ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "OBS reported the stream stopped: the start did not connect."
            );
        }

        return ObsStreamResult.Failed(
            ObsOutputFailure.NotConfirmed,
            "OBS did not report the stream active within "
                + budget.StreamStartTimeout.TotalSeconds.ToString("0.#")
                + " s."
        );
    }

    /// <summary>
    /// The start is still running: OBS reported neither started nor stopped since StartStream,
    /// the websocket did not drop, and it was sent less than <see cref="StreamStartPendingLimit"/>
    /// ago. Null when no start is running.
    /// </summary>
    private DateTimeOffset? PendingStreamStart()
    {
        lock (gate)
        {
            if (streamStartedAt is DateTimeOffset sent && now() - sent < StreamStartPendingLimit)
            {
                return sent;
            }

            streamStartedAt = null;
            return null;
        }
    }

    private void EndStreamStart()
    {
        lock (gate)
        {
            streamStartedAt = null;
        }
    }

    /// <summary>
    /// Active is <see cref="ObsOutputFailure.None"/>. OBS reporting the output stopped after it
    /// reported it starting is <see cref="ObsOutputFailure.NotConfirmed"/>: the start failed.
    /// </summary>
    private ObsOutputFailure? ProbeStreamStart()
    {
        if (!socket.IsConnected)
        {
            return ObsOutputFailure.Disconnected;
        }

        if (socket.IsStreamActive())
        {
            return ObsOutputFailure.None;
        }

        lock (gate)
        {
            return streamStoppedSeen ? ObsOutputFailure.NotConfirmed : null;
        }
    }

    private ObsStreamResult StopStreamCore(Action ensureConnected)
    {
        EnsureIdentified(ensureConnected);
        if (!socket.IsConnected)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected."
            );
        }

        if (!socket.IsStreamActive())
        {
            return ObsStreamResult.ConfirmedInactive();
        }

        logger.LogInformation("Stopping OBS stream.");
        socket.StopStream();
        if (!socket.IsStreamActive())
        {
            return ObsStreamResult.ConfirmedInactive();
        }

        ObsOutputFailure waited = WaitUntil(() => ProbeStream(active: false), budget.StopTimeout);
        if (waited == ObsOutputFailure.None && !socket.IsStreamActive())
        {
            return ObsStreamResult.ConfirmedInactive();
        }

        if (waited == ObsOutputFailure.Disconnected)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before the stream was confirmed inactive."
            );
        }

        return ObsStreamResult.Failed(
            ObsOutputFailure.Timeout,
            "Timed out waiting for OBS to report the stream inactive."
        );
    }

    private ObsOutputFailure? ProbeStream(bool active)
    {
        if (!socket.IsConnected)
        {
            return ObsOutputFailure.Disconnected;
        }

        if (socket.IsStreamActive() == active)
        {
            return ObsOutputFailure.None;
        }

        return null;
    }

    private ObsRecordingResult StartCore(
        Attempt attempt,
        Action ensureConnected,
        int? replayId,
        string reason,
        Action prepareOutput,
        Func<ObsSelectionResult> verifySelection
    )
    {
        if (ownsRecording)
        {
            return SplitSeen()
                ? ObsRecordingResult.Failed(ObsOutputFailure.SplitFile, "OBS split the recording.")
                : ObsRecordingResult.Started();
        }

        EnsureIdentified(ensureConnected);
        if (DisconnectSeen() || !socket.IsConnected)
        {
            return ObsRecordingResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected."
            );
        }

        // Once per start, before a foreign recording is stopped or a new one is started.
        if (!attempt.SelectionChecked)
        {
            ObsSelectionResult selection = verifySelection?.Invoke();
            attempt.SelectionChecked = true;
            if (selection is { Ok: false })
            {
                return ObsRecordingResult.Failed(
                    ObsOutputFailure.SelectionMismatch,
                    selection.Detail,
                    selection.Reason
                );
            }
        }

        if (socket.IsRecording() && !attempt.Called)
        {
            if (!attempt.ForeignStopRequested)
            {
                logger.LogInformation(
                    "OBS is already recording. Replay {ReplayId} will stop that recording without closing OBS, then start its own. The existing file is not adopted.",
                    replayId
                );
                socket.StopRecord();
                attempt.ForeignStopRequested = true;
            }

            if (socket.IsRecording())
            {
                ObsOutputFailure released = WaitUntil(ProbeForeignStopped, budget.StopTimeout);
                if (
                    released == ObsOutputFailure.Disconnected
                    || DisconnectSeen()
                    || !socket.IsConnected
                )
                {
                    return ObsRecordingResult.Failed(
                        ObsOutputFailure.Disconnected,
                        "OBS websocket disconnected before the existing recording stopped."
                    );
                }

                if (socket.IsRecording())
                {
                    return ObsRecordingResult.Failed(
                        ObsOutputFailure.AlreadyRecording,
                        "OBS kept a recording this process did not start. It was not adopted, and OBS was not closed."
                    );
                }
            }

            ClearFlags();
        }

        if (socket.IsRecording())
        {
            return ConfirmStarted(replayId);
        }

        // A directory change is ignored while another recording is still active.
        prepareOutput?.Invoke();

        if (!attempt.Called)
        {
            SetAcceptSplit(true);
            logger.LogInformation(
                "Starting OBS recording for replay {ReplayId} ({Reason}). The file starts on the match and stops before the report scenes.",
                replayId,
                string.IsNullOrWhiteSpace(reason) ? "every replay" : reason
            );
            // Before the request: a spectate killed while OBS confirms still leaves its claim.
            Claim(replayId);
            socket.StartRecord();
            attempt.Called = true;
        }

        if (SplitSeen())
        {
            TakeOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.SplitFile,
                "OBS split the recording."
            );
        }

        if (socket.IsRecording() || ReadActive())
        {
            return ConfirmStarted(replayId);
        }

        ObsOutputFailure waited = WaitUntil(ProbeStart, budget.StartTimeout);
        if (waited == ObsOutputFailure.SplitFile || SplitSeen())
        {
            TakeOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.SplitFile,
                "OBS split the recording."
            );
        }

        if (waited == ObsOutputFailure.Disconnected)
        {
            return ObsRecordingResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before recording was confirmed."
            );
        }

        if (waited == ObsOutputFailure.None)
        {
            return ConfirmStarted(replayId);
        }

        return ObsRecordingResult.Failed(
            ObsOutputFailure.NotConfirmed,
            "OBS did not report recording active."
        );
    }

    private ObsRecordingResult ConfirmStarted(int? replayId)
    {
        TakeOwnership();
        logger.LogInformation("OBS recording is active for replay {ReplayId}.", replayId);
        return ObsRecordingResult.Started();
    }

    private ObsRecordingResult StopCore(Action ensureConnected, int? replayId)
    {
        if (!ownsRecording)
        {
            return ObsRecordingResult.Failed(
                ObsOutputFailure.NotOwned,
                "This process does not own the OBS recording."
            );
        }

        if (SplitSeen())
        {
            return StopSplit();
        }

        if (DisconnectSeen() || !socket.IsConnected)
        {
            ReleaseOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before the recording finalized."
            );
        }

        EnsureIdentified(ensureConnected);
        string pending = ReadStoppedPath();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            return Finish(pending, replayId);
        }

        logger.LogInformation("Stopping OBS recording for replay {ReplayId}.", replayId);
        string path = socket.StopRecord();
        if (SplitSeen())
        {
            ReleaseOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.SplitFile,
                "OBS split the recording."
            );
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            return Finish(path, replayId);
        }

        pending = ReadStoppedPath();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            return Finish(pending, replayId);
        }

        ObsOutputFailure waited = WaitUntil(ProbeStop, budget.StopTimeout);
        if (waited == ObsOutputFailure.SplitFile || SplitSeen())
        {
            ReleaseOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.SplitFile,
                "OBS split the recording."
            );
        }

        if (waited == ObsOutputFailure.Disconnected)
        {
            ReleaseOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before the recording finalized."
            );
        }

        pending = ReadStoppedPath();
        if (waited == ObsOutputFailure.None && !string.IsNullOrWhiteSpace(pending))
        {
            return Finish(pending, replayId);
        }

        if (StoppedSeen())
        {
            ReleaseOwnership();
            // OBS reported the output stopped, so nothing is left running for services stop.
            Unclaim();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "OBS stopped recording without an output path."
            );
        }

        ReleaseOwnership();
        return ObsRecordingResult.Failed(
            ObsOutputFailure.Timeout,
            "Timed out waiting for the finalized OBS recording path."
        );
    }

    private ObsRecordingResult StopSplit()
    {
        try
        {
            socket.StopRecord();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not stop a split OBS recording.");
        }

        ReleaseOwnership();
        return ObsRecordingResult.Failed(ObsOutputFailure.SplitFile, "OBS split the recording.");
    }

    private ObsRecordingResult Finish(string path, int? replayId)
    {
        // OBS gave the stopped file's path: the recording is no longer running.
        Unclaim();
        if (SplitSeen())
        {
            ReleaseOwnership();
            return ObsRecordingResult.Failed(
                ObsOutputFailure.SplitFile,
                "OBS split the recording."
            );
        }

        ReleaseOwnership();
        logger.LogInformation(
            "OBS recording for replay {ReplayId} finalized at {Path}.",
            replayId,
            path
        );
        return ObsRecordingResult.FinalizedAt(path);
    }

    private void EnsureIdentified(Action ensureConnected)
    {
        if (socket.IsIdentified)
        {
            return;
        }

        ensureConnected?.Invoke();
        if (!socket.IsIdentified)
        {
            throw new TimeoutException("OBS websocket did not identify.");
        }
    }

    private ObsOutputFailure? ProbeForeignStopped()
    {
        if (DisconnectSeen() || !socket.IsConnected)
        {
            return ObsOutputFailure.Disconnected;
        }

        if (!socket.IsRecording())
        {
            return ObsOutputFailure.None;
        }

        return null;
    }

    private ObsOutputFailure? ProbeStart()
    {
        if (SplitSeen())
        {
            return ObsOutputFailure.SplitFile;
        }

        if (DisconnectSeen() || !socket.IsConnected)
        {
            return ObsOutputFailure.Disconnected;
        }

        if (ReadActive() || socket.IsRecording())
        {
            return ObsOutputFailure.None;
        }

        return null;
    }

    private ObsOutputFailure? ProbeStop()
    {
        if (SplitSeen())
        {
            return ObsOutputFailure.SplitFile;
        }

        if (DisconnectSeen() || !socket.IsConnected)
        {
            return ObsOutputFailure.Disconnected;
        }

        if (!string.IsNullOrWhiteSpace(ReadStoppedPath()))
        {
            return ObsOutputFailure.None;
        }

        return null;
    }

    private ObsOutputFailure WaitUntil(Func<ObsOutputFailure?> ready, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (true)
        {
            ObsOutputFailure? found = ready();
            if (found.HasValue)
            {
                return found.Value;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return ObsOutputFailure.Timeout;
            }

            TimeSpan slice = budget.PollInterval < remaining ? budget.PollInterval : remaining;
            wake.Wait(slice);
            lock (gate)
            {
                wake.Reset();
            }
        }
    }

    // retry is asked after an exception: false ends the attempts there. Null retries every one.
    private T Execute<T>(
        Func<T> action,
        Func<Exception, T> failed,
        string operation,
        Func<bool> retry = null
    )
    {
        try
        {
            return ResilienceRetry
                .Constant<T>(
                    budget.RetryCount,
                    budget.RetryDelay,
                    outcome => outcome.Exception != null && (retry == null || retry()),
                    args =>
                        logger.LogWarning(
                            args.Outcome.Exception,
                            "Could not {Operation} (attempt {Attempt}).",
                            operation,
                            args.AttemptNumber + 1
                        )
                )
                .Execute(action);
        }
        catch (Exception e)
        {
            logger.LogError(e, "There was an error during OBS {Operation}.", operation);
            return failed(e);
        }
    }

    private void OnSignal(object sender, ObsRecordSignal signal)
    {
        if (signal == null)
        {
            return;
        }

        lock (gate)
        {
            switch (signal.Kind)
            {
                case ObsRecordSignalKind.Started:
                    if (acceptSplit || ownsRecording)
                    {
                        activeSeen = true;
                    }

                    break;
                case ObsRecordSignalKind.Stopped:
                    if (acceptSplit || ownsRecording)
                    {
                        stoppedSeen = true;
                        if (!string.IsNullOrWhiteSpace(signal.OutputPath))
                        {
                            stoppedPath = signal.OutputPath;
                        }
                    }

                    break;
                case ObsRecordSignalKind.Split:
                    if (acceptSplit)
                    {
                        splitSeen = true;
                    }

                    break;
                case ObsRecordSignalKind.Disconnected:
                    disconnectSeen = true;
                    // A new connection is a new OBS, or one that answers again: no start runs.
                    streamStartedAt = null;
                    break;
                case ObsRecordSignalKind.StreamStarting:
                    if (streamStartedAt != null)
                    {
                        streamStarting = true;
                    }

                    break;
                case ObsRecordSignalKind.StreamStarted:
                    streamStartedAt = null;
                    break;
                case ObsRecordSignalKind.StreamStopped:
                    // Only a stop after this start began: a late one from an earlier StopStream
                    // is not this start's failure.
                    if (streamStarting)
                    {
                        streamStoppedSeen = true;
                        streamStarting = false;
                        streamStartedAt = null;
                    }

                    break;
            }

            wake.Set();
        }
    }

    private void LogFailure(ObsRecordingResult result, int? replayId, string action)
    {
        if (
            result == null
            || result.Succeeded
            || result.Failure == ObsOutputFailure.NotOwned
            || result.Failure == ObsOutputFailure.NotRequested
            // The coordinator already logged which profile or collection is wrong.
            || result.Failure == ObsOutputFailure.SelectionMismatch
        )
        {
            return;
        }

        logger.LogWarning(
            "OBS recording {Action} failed for replay {ReplayId} ({Failure}). {Detail}",
            action,
            replayId,
            result.Failure,
            result.Detail
        );
    }

    /// <summary>
    /// Records that this process asked OBS to record. A failed or unconfirmed start keeps the
    /// claim: OBS may still be recording, and services stop or the next spectate checks it against
    /// OBS before it acts.
    /// </summary>
    private void Claim(int? replayId)
    {
        if (claims == null)
        {
            return;
        }

        try
        {
            claims.Save(RecordingClaim.ForThisProcess(replayId, now()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not write the OBS recording claim {Path}. If spectate is killed before replay {ReplayId} ends, neither services stop nor the next spectate will find this recording.",
                claims.FilePath,
                replayId
            );
        }
    }

    private void Unclaim()
    {
        if (claims == null)
        {
            return;
        }

        try
        {
            claims.Clear();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not delete the OBS recording claim {Path}. services stop or the next spectate checks it against OBS and deletes it.",
                claims.FilePath
            );
        }
    }

    private void TakeOwnership()
    {
        lock (gate)
        {
            ownsRecording = true;
        }
    }

    private void ReleaseOwnership()
    {
        lock (gate)
        {
            ownsRecording = false;
            acceptSplit = false;
        }
    }

    private void ClearFlags()
    {
        lock (gate)
        {
            activeSeen = false;
            stoppedSeen = false;
            stoppedPath = null;
            disconnectSeen = false;
            splitSeen = false;
            acceptSplit = false;
            wake.Reset();
        }
    }

    private void SetAcceptSplit(bool value)
    {
        lock (gate)
        {
            acceptSplit = value;
        }
    }

    private bool ReadActive()
    {
        lock (gate)
        {
            return activeSeen;
        }
    }

    private bool SplitSeen()
    {
        lock (gate)
        {
            return splitSeen;
        }
    }

    private bool DisconnectSeen()
    {
        lock (gate)
        {
            return disconnectSeen;
        }
    }

    private bool StoppedSeen()
    {
        lock (gate)
        {
            return stoppedSeen;
        }
    }

    private string ReadStoppedPath()
    {
        lock (gate)
        {
            return stoppedPath;
        }
    }

    private sealed class Attempt
    {
        public bool Called { get; set; }

        public bool ForeignStopRequested { get; set; }

        public bool SelectionChecked { get; set; }
    }
}
