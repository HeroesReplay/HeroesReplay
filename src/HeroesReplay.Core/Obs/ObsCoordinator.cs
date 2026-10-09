using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// One owner for the OBS process, websocket, waiting scene, stream, and recording
/// desired state, and for keeping every microphone muted (#314). Replay sessions use this
/// coordinator and its socket.
/// </summary>
internal sealed class ObsCoordinator
{
    /// <summary>The pause after HeroesReplay starts OBS, before the first identify.</summary>
    internal static readonly TimeSpan LaunchSettle = TimeSpan.FromSeconds(5);

    /// <summary>The pause between identify attempts while an OBS HeroesReplay started comes up.</summary>
    internal static readonly TimeSpan StartupRetryPause = TimeSpan.FromSeconds(2);

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
    private readonly TimeSpan startupIdentifyTimeout;
    private readonly Func<DateTimeOffset> now;
    private readonly ObsCrashSentinel sentinel;
    private readonly ObsMicrophoneMute microphones;
    private readonly Action stateChanged;
    private readonly ObsStreamRecovery recovery;

    // The watchdog reconciles the stream while the spectator switches scenes on its own thread.
    private readonly object stateGate = new();
    private readonly object healthGate = new();
    private volatile ObsRuntimeSnapshot state;
    private ObsStreamHealth health;
    private DateTimeOffset? stuckNoted;
    private bool waitingSceneMissingLogged;
    private string sceneRequested;
    private DateTimeOffset? startupDeadline;
    private DateTimeOffset launchedAt;
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
        Func<ObsValidation> preflight = null,
        TimeSpan? startupIdentifyTimeout = null,
        Func<DateTimeOffset> now = null,
        ObsCrashSentinel sentinel = null,
        IObsMicrophoneSession microphones = null,
        Action stateChanged = null
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
        this.startupIdentifyTimeout =
            startupIdentifyTimeout is TimeSpan startup && startup > TimeSpan.Zero
                ? startup
                : TimeSpan.FromSeconds(60);
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.sentinel = sentinel;
        // No microphone session means nothing is muted.
        this.microphones = microphones == null ? null : new ObsMicrophoneMute(logger, microphones);
        this.stateChanged = stateChanged;
        recovery = new ObsStreamRecovery(
            settings.OBS?.StreamStuckAfter ?? ObsStreamRecovery.DefaultStuckAfter
        );
    }

    public ObsRuntimeSnapshot State => state;

    public bool IsIdentified => socket.IsIdentified;

    /// <summary>
    /// Puts <paramref name="scene"/> on the program output. Once OBS accepts it, it is both the
    /// scene the spectator asked for and the scene on air in <see cref="State"/>, so status.json
    /// follows each switch rather than the scene the session started on (#282). The new state
    /// goes to the status writer at once, not at the watchdog's next tick (#357). A refused
    /// scene throws and leaves <see cref="State"/> as it was.
    /// </summary>
    public void SelectScene(string scene)
    {
        socket.SelectProgramScene(scene);
        if (!SceneSelected(scene))
        {
            // No reconcile has run yet (streaming is off, or it is still to come): read the
            // rest of OBS once so the scene has a snapshot to live in.
            Remember(null, scene);
        }

        Publish();
    }

    /// <summary>False when there is no <see cref="State"/> yet to show the scene.</summary>
    private bool SceneSelected(string scene)
    {
        lock (stateGate)
        {
            sceneRequested = scene;
            if (state == null)
            {
                return false;
            }

            state = state with { SceneDesired = scene, SceneActual = scene };
            return true;
        }
    }

    /// <summary>
    /// Tells the status writer that <see cref="State"/> changed. It reads the newest state and
    /// writes status.json only when an OBS field changed. A write that fails is logged; the
    /// scene stays on air.
    /// </summary>
    private void Publish()
    {
        if (stateChanged == null || state == null)
        {
            return;
        }

        try
        {
            stateChanged();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write the OBS state to status.json.");
        }
    }

    public bool StreamIsDesired() => ObsDesired.StreamIsDesired(settings.OBS);

    public void EnsureIdentified()
    {
        if (socket.IsIdentified)
        {
            return;
        }

        PrepareProcess();
        Identify();
        if (!socket.IsIdentified)
        {
            throw new TimeoutException("OBS websocket at " + Endpoint() + " did not identify.");
        }
    }

    /// <summary>
    /// Starts a replay's session. Identifies OBS (throws when it does not), lets
    /// <paramref name="collectionReady"/> put the collection in place, then mutes every
    /// microphone (#314). The live collection swap comes first because a collection brings its
    /// own global audio devices. Only the identify throws. Either way, OBS is read once and
    /// the status writer gets it, so status.json shows OBS from the session's start (#357).
    /// </summary>
    public void BeginSession(Action collectionReady = null)
    {
        try
        {
            EnsureIdentified();
            microphones?.NewSession();
            try
            {
                collectionReady?.Invoke();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "The OBS collection was not prepared for this replay.");
            }

            MuteMicrophones(ObsMicrophoneMute.AtSessionStart);
        }
        finally
        {
            // The process, stream, recording, and program scene, read again. The last stream
            // result stays, so a stream block the watchdog found stays until its next reconcile.
            Remember(state?.Stream, ReadScene());
            Publish();
        }
    }

    /// <summary>
    /// <c>OBS:MuteMicrophones</c>: mutes every microphone OBS has. A failure is logged and the
    /// caller goes on; the decision was mute, not block (#314).
    /// </summary>
    private void MuteMicrophones(string moment)
    {
        if (microphones == null || settings.OBS?.MuteMicrophones != true)
        {
            return;
        }

        microphones.MuteAll(moment);
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

    /// <summary>
    /// The stream's health now (#395): one GetStreamStatus read when the websocket is identified,
    /// compared with the read before it. Only <see cref="ObsStreamState.Live"/> is a stream viewers
    /// see; an output that is active but reconnecting or frozen is not. Never throws.
    /// </summary>
    public ObsStreamHealth ReadStreamHealth()
    {
        ObsStreamSample? sample = null;
        string failure = "OBS websocket is not identified.";
        try
        {
            if (socket.IsIdentified)
            {
                sample = socket.ReadStream();
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read OBS stream status.");
            failure = "GetStreamStatus failed. " + e.Message;
        }

        lock (healthGate)
        {
            DateTimeOffset at = now();
            health = sample is ObsStreamSample read
                ? ObsStreamHealth.Next(health, read, at)
                : ObsStreamHealth.Unknown(health, failure, at);
            if (health.IsLive)
            {
                recovery.Recovered();
            }

            return health;
        }
    }

    /// <summary>
    /// Keeps a desired stream live (#395). A live stream is left alone. An inactive one gets the
    /// guarded start: the waiting scene, the profile and collection check, the preflight, the
    /// microphone mute, then StartStream. One that stays reconnecting, or active with frozen bytes,
    /// longer than <c>OBS:StreamStuckAfter</c> is stopped, confirmed inactive, and started the same
    /// way. A failed start or restart waits 1, 2, then 5 minutes before the next, and each attempt
    /// logs at most one warning. Nothing is sent unless streaming is enabled and this machine is
    /// armed.
    /// </summary>
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

        ObsStreamHealth read = ReadStreamHealth();
        switch (recovery.Decide(read, now()))
        {
            case ObsStreamStep.Keep:
                stuckNoted = null;
                return Remember(ObsStreamResult.ConfirmedActive(), ReadScene());
            case ObsStreamStep.NotRead:
                logger.LogWarning(
                    "OBS stream status was not read, so the stream was neither started nor stopped. {Detail}",
                    read.Detail
                );
                return Remember(
                    ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, read.Detail),
                    ReadScene()
                );
            case ObsStreamStep.WaitForObs:
                return Remember(WaitForObs(read), ReadScene());
            case ObsStreamStep.Backoff:
                return Remember(
                    ObsStreamResult.Waiting(
                        read.Detail
                            + " The last stream attempt failed; the next is due at "
                            + Clock(recovery.NextAttemptAt)
                            + "."
                    ),
                    ReadScene()
                );
            case ObsStreamStep.Restart:
                return Restart(read);
            default:
                return Start();
        }
    }

    /// <summary>
    /// The output is reconnecting or frozen, under <c>OBS:StreamStuckAfter</c>: OBS's own reconnect
    /// owns it. Logged once per stretch.
    /// </summary>
    private ObsStreamResult WaitForObs(ObsStreamHealth stuck)
    {
        if (stuckNoted != stuck.StuckSince)
        {
            stuckNoted = stuck.StuckSince;
            logger.LogInformation(
                "OBS stream is {State}. {Detail} OBS's own reconnect has {StuckAfter} (OBS:StreamStuckAfter) before the stream is stopped and started again.",
                stuck.State,
                stuck.Detail,
                recovery.StuckAfter
            );
        }

        return ObsStreamResult.Waiting(
            stuck.Detail
                + " OBS's own reconnect has until "
                + Clock(stuck.StuckSince + recovery.StuckAfter)
                + " before the stream is restarted."
        );
    }

    /// <summary>The output is inactive: the guarded start, with a backoff after a failure.</summary>
    private ObsRuntimeSnapshot Start()
    {
        ObsStreamResult stream = StartInactive(out bool attempted);
        if (Live(stream))
        {
            logger.LogInformation("OBS stream is active.");
        }
        else if (attempted)
        {
            TimeSpan delay = recovery.Failed(now());
            logger.LogWarning(
                "OBS stream was not confirmed live ({Failure}). {Detail} Next attempt in {Delay}.",
                stream?.Failure,
                stream?.Detail,
                delay
            );
        }

        return Remember(stream, ReadScene());
    }

    /// <summary>
    /// The output stayed reconnecting or frozen past <c>OBS:StreamStuckAfter</c> (#395):
    /// StopStream, confirm it inactive, then the guarded start. One warning per attempt, with the
    /// cause code, the frozen byte count, and how long it was stuck. A restart that does not end
    /// live waits for the backoff. The spectator's scene goes back right after StartStream, so a
    /// restart mid-match does not leave the waiting scene on air.
    /// </summary>
    private ObsRuntimeSnapshot Restart(ObsStreamHealth stuck)
    {
        TimeSpan stuckFor = stuck.StuckFor(now());
        ObsStreamResult stopped = recording.StopStreaming(EnsureIdentified);
        ObsStreamResult result;
        if (stopped is { Succeeded: true, Active: false })
        {
            // A new output starts a new stretch: the stuck one's bytes and clock are done.
            ReadStreamHealth();
            result = StartInactive(out _);
        }
        else
        {
            result = ObsStreamResult.Failed(
                stopped?.Failure is ObsOutputFailure failure && failure != ObsOutputFailure.None
                    ? failure
                    : ObsOutputFailure.NotConfirmed,
                "StopStream was not confirmed. " + stopped?.Detail,
                stuck.CauseCode
            );
        }

        if (Live(result))
        {
            logger.LogWarning(
                "OBS stream was stuck {State} for {StuckFor} ({CauseCode}): {Bytes} bytes sent, frozen since {StuckSince}. Stopped it and started it again; the stream is live.",
                stuck.State,
                stuckFor,
                stuck.CauseCode,
                stuck.Bytes,
                stuck.StuckSince
            );
        }
        else
        {
            TimeSpan delay = recovery.Failed(now());
            logger.LogWarning(
                "OBS stream was stuck {State} for {StuckFor} ({CauseCode}): {Bytes} bytes sent, frozen since {StuckSince}. The restart did not end live ({Failure}): {Detail} Next attempt in {Delay}.",
                stuck.State,
                stuckFor,
                stuck.CauseCode,
                stuck.Bytes,
                stuck.StuckSince,
                result?.Failure,
                result?.Detail,
                delay
            );
        }

        return Remember(result, ReadScene());
    }

    /// <summary>A start that OBS confirmed, and whose output then reads live, not reconnecting.</summary>
    private bool Live(ObsStreamResult started) =>
        started is { Succeeded: true, Active: true } && ReadStreamHealth().IsLive;

    /// <summary>
    /// The guarded start of an inactive output: the waiting scene, the profile and collection
    /// check, the preflight, the microphone mute, then StartStream with its short retries.
    /// <paramref name="attempted"/> is true once StartStream was sent. The stream starts on the
    /// waiting scene, and right after StartStream the scene the spectator last asked for (the
    /// match, a report) goes back on air (#395). On 2026-10-09 this start left
    /// <c>waiting-screen</c> on air for the last 21 minutes of a match.
    /// </summary>
    private ObsStreamResult StartInactive(out bool attempted)
    {
        attempted = false;
        string desired;
        lock (stateGate)
        {
            desired = sceneRequested;
        }

        string scene = settings.OBS?.WaitingSceneName;
        if (string.IsNullOrWhiteSpace(scene))
        {
            if (!waitingSceneMissingLogged)
            {
                waitingSceneMissingLogged = true;
                logger.LogWarning(
                    "OBS stream was not started: OBS:WaitingSceneName is not configured."
                );
            }

            return ObsStreamResult.Failed(
                ObsOutputFailure.NotConfirmed,
                "Waiting scene is not configured."
            );
        }

        // Before the scene changes: a wrong collection must not be touched either.
        ObsSelectionResult selection = CheckSelection();
        if (!selection.Ok)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.SelectionMismatch,
                selection.Detail,
                selection.Reason
            );
        }

        ObsStreamResult blocked = Preflight();
        if (blocked != null)
        {
            return blocked;
        }

        try
        {
            socket.SelectProgramScene(scene);
            SceneSelected(scene);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not select the OBS waiting scene.");
            return ObsStreamResult.Failed(ObsOutputFailure.NotConfirmed, e.Message);
        }

        // Last thing before the stream goes out: a microphone unmuted since the session began
        // must not go live with it.
        MuteMicrophones(ObsMicrophoneMute.BeforeStartStream);
        attempted = true;
        ObsStreamResult started = ObsBackoff.Run(
            backoff.Delays(),
            () => recording.StartStreaming(EnsureIdentified),
            wait
        );
        // Whether or not the start was confirmed: a recording takes the program output too.
        PutBack(desired, scene);
        return started;
    }

    /// <summary>
    /// Puts the spectator's scene back after the start showed the waiting scene, unless the
    /// spectator asked for another one meanwhile (then its choice is already on air). Nothing
    /// to do when the spectator wanted the waiting scene or had not asked for one yet.
    /// </summary>
    private void PutBack(string desired, string waiting)
    {
        if (
            string.IsNullOrWhiteSpace(desired)
            || string.Equals(desired, waiting, StringComparison.Ordinal)
        )
        {
            return;
        }

        lock (stateGate)
        {
            if (!string.Equals(sceneRequested, waiting, StringComparison.Ordinal))
            {
                return;
            }
        }

        try
        {
            socket.SelectProgramScene(desired);
            SceneSelected(desired);
            logger.LogInformation(
                "Put {Scene} back on the program output after StartStream; the stream started on {Waiting}.",
                desired,
                waiting
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not put {Scene} back after StartStream. {Waiting} stays on air until the spectator changes the scene.",
                desired,
                waiting
            );
        }
    }

    private static string Clock(DateTimeOffset? at) =>
        at is DateTimeOffset value ? value.ToUniversalTime().ToString("HH:mm:ss") + "Z" : "-";

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
            Identify();
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

        RemoveStaleSentinels();
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
            // OBS this process started gets a startup window: identify is retried until it ends.
            launchedAt = now();
            startupDeadline = launchedAt + startupIdentifyTimeout;
            wait?.Invoke(LaunchSettle);
        }
    }

    /// <summary>
    /// One identify attempt when OBS was already running. While an OBS this coordinator started
    /// is inside its startup window (<see cref="OBSSettings.StartupIdentifyTimeout"/>), attempts
    /// repeat until OBS identifies, the window ends, or the started process exits.
    /// </summary>
    private void Identify()
    {
        if (!InStartupWindow())
        {
            socket.Connect(Endpoint(), Password(), identifyTimeout);
            return;
        }

        DateTimeOffset deadline = startupDeadline.Value;
        int attempts = 0;
        Exception last = null;
        while (true)
        {
            TimeSpan remaining = deadline - now();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            attempts++;
            try
            {
                socket.Connect(Endpoint(), Password(), Shorter(identifyTimeout, remaining));
                last = null;
            }
            catch (Exception e)
            {
                last = e;
                logger.LogDebug(
                    e,
                    "OBS websocket identify attempt {Attempt} failed while OBS starts.",
                    attempts
                );
            }

            if (socket.IsIdentified)
            {
                startupDeadline = null;
                logger.LogInformation(
                    "OBS websocket identified {Elapsed:0.0}s after HeroesReplay started OBS (attempt {Attempt}).",
                    (now() - launchedAt).TotalSeconds,
                    attempts
                );
                return;
            }

            if (!process.IsOwned)
            {
                logger.LogWarning(
                    "OBS exited before its websocket identified (attempt {Attempt}).",
                    attempts
                );
                break;
            }

            remaining = deadline - now();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            wait?.Invoke(Shorter(StartupRetryPause, remaining));
        }

        startupDeadline = null;
        throw new TimeoutException(
            "OBS websocket at "
                + Endpoint()
                + " did not identify within "
                + startupIdentifyTimeout
                + " after HeroesReplay started OBS ("
                + attempts
                + " attempts).",
            last
        );
    }

    /// <summary>
    /// A run sentinel left by an OBS that did not exit cleanly stops the launch on the Crash
    /// Detected dialog. The sentinel itself checks again that no OBS runs before it deletes.
    /// </summary>
    private void RemoveStaleSentinels()
    {
        try
        {
            sentinel?.RemoveStale();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not clear stale OBS crash sentinels before the launch.");
        }
    }

    private bool InStartupWindow() => startupDeadline is DateTimeOffset end && now() < end;

    private static TimeSpan Shorter(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private ObsRuntimeSnapshot Remember(ObsStreamResult stream, string sceneActual)
    {
        bool running = ReadRunning();
        ObsStreamHealth read = ReadStreamHealth();
        // Live only: an output that is active but reconnecting or frozen is not on air (#395).
        // A start OBS just confirmed still counts when the read after it failed.
        bool streamActive =
            read.IsLive
            || (
                read.State == ObsStreamState.Unknown && stream is { Succeeded: true, Active: true }
            );
        bool recordingActive = ReadRecording();
        lock (stateGate)
        {
            state = ObsDesired.Capture(
                settings.OBS,
                running,
                process.IsOwned,
                lastLaunch,
                socket.IsIdentified,
                sceneActual,
                streamActive,
                recordingDesired,
                recordingActive,
                stream,
                sceneRequested,
                read
            );
            return state;
        }
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
