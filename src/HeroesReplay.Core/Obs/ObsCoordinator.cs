using System;
using HeroesReplay.Core.Configuration;
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
    private bool recordingDesired;
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
        Action beforeLaunch = null
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
            prepareOutput
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
            exists
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

    private string Refusal() => TwitchIngestGuard.Refusal(SessionMedia.ShouldStream(settings.OBS));

    private string Endpoint() =>
        string.IsNullOrWhiteSpace(settings.OBS?.WebSocketEndpoint)
            ? "ws://127.0.0.1:4455"
            : settings.OBS.WebSocketEndpoint;

    private string Password() => settings.OBS?.WebSocketPassword ?? string.Empty;
}
