using System;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// When spectate starts, before its first replay, it stops the OBS recording an earlier spectate
/// claimed and left running (#342). The supervisor restarts a crashed or stale spectate, or one
/// whose launch stalled, without <c>services stop</c>, so that recording would otherwise grow
/// until a later replay started its own, and forever when no later replay records. It is the
/// <c>services stop</c> check (<see cref="OrphanRecording.Stop"/>): the claimant's pid must be
/// dead (pid and start time, <see cref="ProcessTable"/>) and OBS must have recorded for about as
/// long as the claim says. It sends <c>StopRecord</c> only, never <c>StopStream</c>, and its short
/// websocket session is closed before the first replay opens the spectator's own. It logs one
/// line and never throws.
/// </summary>
public sealed class OrphanRecordingOnStart
{
    private readonly OBSSettings settings;
    private readonly ILogger logger;
    private readonly RecordingClaimStore claims;
    private readonly Func<bool> obsRunning;
    private readonly Func<int, ProcessTableEntry> findProcess;
    private readonly Func<IObsRecordStopSession> open;
    private readonly TimeProvider time;
    private readonly Action<TimeSpan> wait;

    public OrphanRecordingOnStart(AppSettings settings, ILogger<OrphanRecordingOnStart> logger)
        : this(
            settings?.OBS,
            logger,
            new RecordingClaimStore(RecordingClaimStore.DefaultPath),
            () => NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
            ProcessTable.Find,
            () =>
                new ObsWebsocketRecordStopSessionFactory().Open(
                    settings?.OBS?.WebSocketEndpoint,
                    settings?.OBS?.WebSocketPassword
                ),
            TimeProvider.System,
            Thread.Sleep
        ) { }

    internal OrphanRecordingOnStart(
        OBSSettings settings,
        ILogger logger,
        RecordingClaimStore claims,
        Func<bool> obsRunning,
        Func<int, ProcessTableEntry> findProcess,
        Func<IObsRecordStopSession> open,
        TimeProvider time,
        Action<TimeSpan> wait
    )
    {
        this.settings = settings ?? new OBSSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.claims = claims ?? throw new ArgumentNullException(nameof(claims));
        this.obsRunning = obsRunning ?? throw new ArgumentNullException(nameof(obsRunning));
        this.findProcess = findProcess ?? throw new ArgumentNullException(nameof(findProcess));
        this.open = open ?? throw new ArgumentNullException(nameof(open));
        this.time = time ?? TimeProvider.System;
        this.wait = wait;
    }

    /// <summary>
    /// Checks the claim once. Null when <c>OBS:Enabled</c> is false: spectate sends OBS nothing
    /// then, and <c>services stop</c> still checks the claim.
    /// </summary>
    public OrphanRecordingCheck Run()
    {
        if (!settings.Enabled)
        {
            logger.LogDebug(
                "OBS:Enabled is false, so spectate start did not check for a recording an earlier spectate left running."
            );
            return null;
        }

        OrphanRecordingCheck check;
        try
        {
            check = OrphanRecording.Stop(
                claims,
                obsRunning(),
                findProcess,
                open,
                time.GetUtcNow(),
                wait
            );
        }
        catch (Exception e)
        {
            check = new OrphanRecordingCheck(OrphanRecordingState.Unknown, e.Message);
        }

        logger.Log(
            LevelOf(check.State),
            "Spectate start, OBS recording an earlier spectate claimed: {Result}",
            check.Describe()
        );
        return check;
    }

    private static LogLevel LevelOf(OrphanRecordingState state) =>
        state switch
        {
            OrphanRecordingState.None => LogLevel.Debug,
            OrphanRecordingState.ObsNotRunning
            or OrphanRecordingState.Inactive
            or OrphanRecordingState.NotOwned => LogLevel.Information,
            // Stopped: a spectate died with its recording running. The rest left a claimed
            // recording that may still be running.
            _ => LogLevel.Warning,
        };
}
