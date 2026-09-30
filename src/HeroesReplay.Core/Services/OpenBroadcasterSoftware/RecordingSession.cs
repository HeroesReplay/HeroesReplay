using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

/// <summary>
/// Owns at most the recording this process started. Request exceptions leave the retry
/// delegate. Timeouts, disconnects, and split files are failed results.
/// </summary>
internal sealed class RecordingSession
{
    private readonly ILogger logger;
    private readonly IObsRecordSocket socket;
    private readonly ObsRecordingBudget budget;
    private readonly object gate = new();
    private readonly ManualResetEventSlim wake = new(false);
    private volatile bool ownsRecording;
    private bool acceptSplit;
    private bool activeSeen;
    private bool stoppedSeen;
    private bool splitSeen;
    private bool disconnectSeen;
    private string stoppedPath;

    public RecordingSession(ILogger logger, IObsRecordSocket socket, ObsRecordingBudget budget)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.budget = budget ?? ObsRecordingBudget.Default;
        if (this.budget.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        socket.RecordSignal += OnSignal;
    }

    public ObsRecordingResult StartRecording(
        Func<bool> shouldRecord,
        Action ensureConnected,
        int? replayId,
        string reason,
        Action prepareOutput = null
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
            () => StartCore(attempt, ensureConnected, replayId, reason, prepareOutput),
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

    public ObsStreamResult StartStreaming(Action ensureConnected)
    {
        return Execute(
            () => StartStreamCore(ensureConnected),
            error =>
                error is TimeoutException
                    ? ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, error.Message)
                    : ObsStreamResult.Failed(ObsOutputFailure.RequestError, error.Message),
            "start OBS streaming"
        );
    }

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

    private ObsStreamResult StartStreamCore(Action ensureConnected)
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
            return ObsStreamResult.ConfirmedActive();
        }

        logger.LogInformation("Starting OBS stream.");
        socket.StartStream();
        if (socket.IsStreamActive())
        {
            return ObsStreamResult.ConfirmedActive();
        }

        ObsOutputFailure waited = WaitUntil(() => ProbeStream(active: true), budget.StartTimeout);
        if (waited == ObsOutputFailure.None && socket.IsStreamActive())
        {
            return ObsStreamResult.ConfirmedActive();
        }

        if (waited == ObsOutputFailure.Disconnected)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.Disconnected,
                "OBS websocket disconnected before the stream was confirmed."
            );
        }

        return ObsStreamResult.Failed(
            ObsOutputFailure.NotConfirmed,
            "OBS did not report the stream active."
        );
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
        Action prepareOutput
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

    private T Execute<T>(Func<T> action, Func<Exception, T> failed, string operation)
    {
        try
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetry(
                    budget.RetryCount,
                    _ => budget.RetryDelay,
                    (exception, _, attempt, _) =>
                        logger.LogWarning(
                            exception,
                            "Could not {Operation} (attempt {Attempt}).",
                            operation,
                            attempt
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
    }
}
