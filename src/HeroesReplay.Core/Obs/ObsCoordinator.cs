using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// One owner for the OBS process, websocket, waiting scene, stream, and recording
/// desired state. Replay sessions use this coordinator and its socket.
/// </summary>
internal sealed class ObsCoordinator
{
    private readonly ILogger logger;
    private readonly AppSettings settings;
    private readonly IObsSession socket;
    private readonly IObsProcess process;
    private readonly RecordingSession recording;
    private readonly ObsBackoff backoff;
    private readonly Action<TimeSpan> wait;
    private readonly TimeSpan identifyTimeout;
    private readonly Action beforeLaunch;
    private readonly Func<bool> streamArmed;
    private readonly Func<ObsValidation> preflight;
    private bool recordingDesired;
    private bool notArmedLogged;
    private bool preflightPassed;
    private string preflightBlockLogged;
    private ObsSelectionResult lastSelection;
    private ObsLaunchDecision lastLaunch = new()
    {
        Kind = ObsLaunchKind.Skipped,
        Detail = "OBS launch has not been decided.",
    };

    public ObsCoordinator(
        ILogger logger,
        AppSettings settings,
        IObsSession socket,
        IObsProcess process,
        RecordingSession recording,
        ObsBackoff backoff,
        Action<TimeSpan> wait,
        TimeSpan identifyTimeout,
        Action beforeLaunch = null,
        Func<bool> streamArmed = null,
        Func<ObsValidation> preflight = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.recording = recording ?? throw new ArgumentNullException(nameof(recording));
        this.backoff = backoff ?? ObsBackoff.Default;
        this.wait = wait;
        this.identifyTimeout =
            identifyTimeout > TimeSpan.Zero ? identifyTimeout : TimeSpan.FromSeconds(10);
        this.beforeLaunch = beforeLaunch;
        // No arm reader means not armed: ingest fails closed.
        this.streamArmed = streamArmed ?? (() => false);
        this.preflight = preflight;
    }

    public ObsRuntimeSnapshot State { get; private set; }

    public bool StreamIsDesired() => ObsDesired.StreamIsDesired(settings.OBS);

    public void EnsureIdentified()
    {
        if (socket.IsIdentified)
        {
            return;
        }

        PrepareProcess();
        socket.Connect(Endpoint(), Password(), identifyTimeout);
        if (!socket.IsIdentified)
        {
            throw new TimeoutException("OBS websocket at " + Endpoint() + " did not identify.");
        }
    }

    public void Disconnect()
    {
        try
        {
            socket.Disconnect();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "OBS disconnect failed.");
        }
    }

    public ObsRecordingResult StartRecording(
        Func<bool> shouldRecord,
        int? replayId,
        string reason,
        Action prepareOutput = null
    )
    {
        recordingDesired = shouldRecord == null || shouldRecord();
        return recording.StartRecording(
            shouldRecord,
            EnsureIdentified,
            replayId,
            reason,
            prepareOutput,
            CheckSelection
        );
    }

    public ObsRecordingResult StopRecording(int? replayId) =>
        recording.StopRecording(EnsureIdentified, replayId);

    public bool IsStreaming()
    {
        try
        {
            return socket.IsIdentified && socket.IsStreamActive();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read OBS stream status.");
            return false;
        }
    }

    public ObsRuntimeSnapshot ReconcileStream()
    {
        if (!StreamIsDesired())
        {
            ObsStreamResult refused = ObsStreamResult.NotRequested(Refusal());
            logger.LogDebug("Skipping OBS StartStream. {Detail}", refused.Detail);
            return Remember(refused, ReadScene());
        }

        // The arm is read every time, so `obs arm` and `obs disarm` apply without a restart.
        if (!Armed())
        {
            ObsStreamResult notArmed = ObsStreamResult.NotArmed();
            if (!notArmedLogged)
            {
                notArmedLogged = true;
                logger.LogWarning(
                    "{Detail} Reason {Reason}. Arm file {Path}.",
                    notArmed.Detail,
                    notArmed.Reason,
                    ObsStreamArm.DefaultPath()
                );
            }

            return Remember(notArmed, ReadScene());
        }

        notArmedLogged = false;
        try
        {
            beforeLaunch?.Invoke();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS collection was not updated before stream reconcile.");
        }

        if (!TryIdentify(out string identifyFailure))
        {
            ObsStreamResult unconfirmed = ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                identifyFailure
            );
            logger.LogWarning("OBS stream was not confirmed. {Detail}", unconfirmed.Detail);
            return Remember(unconfirmed, ReadScene());
        }

        if (ReadStreamActive())
        {
            return Remember(ObsStreamResult.ConfirmedActive(), ReadScene());
        }

        string scene = settings.OBS?.WaitingSceneName;
        if (string.IsNullOrWhiteSpace(scene))
        {
            return Remember(
                ObsStreamResult.Failed(
                    ObsOutputFailure.NotConfirmed,
                    "Waiting scene is not configured."
                ),
                ReadScene()
            );
        }

        // Before the scene changes: a wrong collection must not be touched either.
        ObsSelectionResult selection = CheckSelection();
        if (!selection.Ok)
        {
            return Remember(
                ObsStreamResult.Failed(
                    ObsOutputFailure.SelectionMismatch,
                    selection.Detail,
                    selection.Reason
                ),
                ReadScene()
            );
        }

        ObsStreamResult blocked = Preflight();
        if (blocked != null)
        {
            return Remember(blocked, ReadScene());
        }

        try
        {
            socket.SelectProgramScene(scene);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not select the OBS waiting scene.");
            return Remember(
                ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, e.Message),
                ReadScene()
            );
        }

        ObsStreamResult stream = ObsBackoff.Run(
            backoff.Delays(),
            () => recording.StartStreaming(EnsureIdentified),
            wait
        );
        if (stream == null || !stream.Succeeded)
        {
            logger.LogWarning(
                "OBS stream was not confirmed ({Failure}). {Detail}",
                stream?.Failure,
                stream?.Detail
            );
        }
        else
        {
            logger.LogInformation("OBS stream is active.");
        }

        return Remember(stream, ReadScene());
    }

    public ObsShutdownResult Shutdown()
    {
        ObsShutdownPlan plan = ObsShutdownPlan.For(
            process.IsOwned,
            settings.OBS?.CloseOwnedOnStop == true,
            streamMayBeActive: true
        );
        ObsStreamResult stream = SessionMedia.ShouldStream(settings.OBS)
            ? ConfirmStopWithoutLaunch()
            : ObsStreamResult.NotRequested();
        if (plan.CloseProcess)
        {
            process.CloseOwned();
        }

        Remember(stream, ReadScene());
        return new ObsShutdownResult { Plan = plan, Stream = stream };
    }

    private ObsStreamResult ConfirmStopWithoutLaunch()
    {
        if (!socket.IsIdentified)
        {
            try
            {
                socket.Connect(Endpoint(), Password(), identifyTimeout);
            }
            catch (Exception e)
            {
                return ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, e.Message);
            }
        }

        if (!socket.IsIdentified)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "OBS websocket did not identify. The stream was not confirmed inactive."
            );
        }

        return recording.StopStreaming(() =>
        {
            if (!socket.IsIdentified)
            {
                throw new TimeoutException("OBS websocket did not identify.");
            }
        });
    }

    private bool TryIdentify(out string detail)
    {
        detail = null;
        if (socket.IsIdentified)
        {
            return true;
        }

        try
        {
            PrepareProcess();
            socket.Connect(Endpoint(), Password(), identifyTimeout);
        }
        catch (Exception e)
        {
            detail = e.Message;
            return false;
        }

        if (socket.IsIdentified)
        {
            return true;
        }

        detail = "OBS websocket did not identify.";
        return false;
    }

    private void PrepareProcess()
    {
        OBSSettings obs = settings.OBS;
        string path = ObsLaunchDecision.ResolveExecutable(obs?.ExecutablePath);
        bool exists = false;
        try
        {
            exists = process.ExecutableExists(path);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not check the OBS executable.");
        }

        bool running = false;
        try
        {
            running = process.IsRunning();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not check whether OBS is running.");
        }

        ObsLaunchDecision decision = ObsLaunchDecision.Decide(
            obs?.Enabled == true,
            running,
            path,
            exists,
            ObsNames.Profile(obs),
            ObsNames.SceneCollection(obs)
        );
        if (decision.Kind != ObsLaunchKind.Launch)
        {
            lastLaunch = decision;
            logger.LogDebug("OBS process: {Detail}", decision.Detail);
            return;
        }

        try
        {
            ObsLaunchDecision started = process.Start(decision);
            lastLaunch = started ?? decision;
        }
        catch (Exception e)
        {
            lastLaunch = decision with { Started = false, Detail = e.Message };
            logger.LogWarning(e, "OBS was not started.");
            return;
        }

        logger.LogWarning(
            "OBS launch {Path} {Arguments}. Started={Started}.",
            lastLaunch.ExecutablePath,
            lastLaunch.Arguments,
            lastLaunch.Started
        );
        if (lastLaunch.Started)
        {
            wait?.Invoke(TimeSpan.FromSeconds(5));
        }
    }

    private ObsRuntimeSnapshot Remember(ObsStreamResult stream, string sceneActual)
    {
        State = ObsDesired.Capture(
            settings.OBS,
            ReadRunning(),
            process.IsOwned,
            lastLaunch,
            socket.IsIdentified,
            sceneActual,
            stream != null && stream.Succeeded && stream.Active ? true : ReadStreamActive(),
            recordingDesired,
            ReadRecording(),
            stream
        );
        return State;
    }

    private bool ReadRunning()
    {
        try
        {
            return process.IsRunning();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool ReadStreamActive()
    {
        try
        {
            return socket.IsIdentified && socket.IsStreamActive();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool ReadRecording()
    {
        try
        {
            return socket.IsIdentified && socket.IsRecording();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string ReadScene()
    {
        if (!socket.IsIdentified)
        {
            return null;
        }

        try
        {
            return socket.ProgramScene;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the OBS program scene.");
            return null;
        }
    }

    private string Refusal() =>
        TwitchIngestGuard.Refusal(SessionMedia.ShouldStream(settings.OBS), Armed());

    private bool Armed()
    {
        try
        {
            return streamArmed();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the stream arm. Twitch ingest stays off.");
            return false;
        }
    }

    /// <summary>
    /// Validates the loaded collection once per process, before the first StartStream. A
    /// <see cref="ObsValidator.StreamBlockers"/> error stops the stream (retried on the next
    /// reconcile); every other finding is logged and the stream starts. A preflight that cannot
    /// run does not stop the stream: the selection check above already guards the collection.
    /// </summary>
    private ObsStreamResult Preflight()
    {
        if (preflightPassed || preflight == null)
        {
            return null;
        }

        ObsValidation validation;
        try
        {
            validation = preflight();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS preflight could not run. The stream starts without it.");
            return null;
        }

        ObsFinding blocker = ObsValidator.BlocksStream(validation);
        if (blocker != null)
        {
            if (!string.Equals(preflightBlockLogged, blocker.Code, StringComparison.Ordinal))
            {
                preflightBlockLogged = blocker.Code;
                logger.LogError(
                    "OBS stream was not started. Preflight {Code} {Subject}: {Message}",
                    blocker.Code,
                    blocker.Subject,
                    blocker.Message
                );
            }

            return ObsStreamResult.Failed(
                ObsOutputFailure.PreflightFailed,
                blocker.Message,
                blocker.Code
            );
        }

        preflightPassed = true;
        preflightBlockLogged = null;
        foreach (ObsFinding finding in validation?.Findings ?? [])
        {
            logger.Log(
                finding.Severity == ObsValidator.Error ? LogLevel.Warning : LogLevel.Information,
                "OBS preflight {Severity} {Code} {Subject}: {Message}",
                finding.Severity,
                finding.Code,
                finding.Subject,
                finding.Message
            );
        }

        return null;
    }

    /// <summary>
    /// Reads the active profile and scene collection. Logs an error when the answer changes
    /// to a mismatch, so a wrong selection is reported once rather than on every probe.
    /// </summary>
    private ObsSelectionResult CheckSelection()
    {
        ObsSelectionResult result;
        try
        {
            result = ObsSelection.Check(
                ObsNames.Profile(settings.OBS),
                ObsNames.SceneCollection(settings.OBS),
                socket.CurrentProfile(),
                socket.CurrentSceneCollection()
            );
        }
        catch (Exception e)
        {
            result = ObsSelection.NotRead(e.Message);
        }

        if (!result.Ok && result != lastSelection)
        {
            logger.LogError(
                "OBS output was not started ({Reason}). {Detail}",
                result.Reason,
                result.Detail
            );
        }
        else if (result.Ok && lastSelection is { Ok: false })
        {
            logger.LogInformation("{Detail}", result.Detail);
        }

        lastSelection = result;
        return result;
    }

    private string Endpoint() =>
        string.IsNullOrWhiteSpace(settings.OBS?.WebSocketEndpoint)
            ? "ws://127.0.0.1:4455"
            : settings.OBS.WebSocketEndpoint;

    private string Password() => settings.OBS?.WebSocketPassword ?? string.Empty;
}
