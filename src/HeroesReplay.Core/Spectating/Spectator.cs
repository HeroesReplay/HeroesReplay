using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.TwitchExtension;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectating;

public class Spectator : ISpectator
{
    private readonly IGameController controller;
    private readonly ITalentNotifier talentsNotifier;
    private readonly ILogger<Spectator> logger;
    private readonly AppSettings settings;
    private readonly CancellationTokenProvider consoleTokenProvider;
    private readonly IReplayContext context;
    private readonly SpectatorStatusStore statusStore;
    private readonly IObserverPanelRequests panelRequests;
    private readonly IObsController obsController;
    private readonly Dictionary<Panel, TimeSpan> panelTimes;

    private State State { get; set; }

    private TimeSpan Timer { get; set; }

    private readonly IGameTimer gameTimer;

    private readonly GameTimerLog clockLog;

    private readonly RecordingClock recordingClock;

    private DateTimeOffset? endScreenStarted;

    private TimeSpan lastAdvancedHud = TimeSpan.MinValue;

    private DateTimeOffset lastAdvancedHudAt;

    private DateTimeOffset nextEndScreenProbe;
    private DateTimeOffset lastClockMissLog;

    private bool endScreenSeen;

    private int hungChecks;

    private int missingProcessChecks;

    private DateTimeOffset sessionStarted;

    private bool matchClockSeen;

    private MatchOutcome outcome;

    private readonly MatchCompletion completion = new();

    public bool MatchClockSeen => matchClockSeen;

    public MatchOutcome Outcome => outcome;

    private ContextData Data => context.Current;

    private CancellationTokenSource CancelSessionSource { get; set; }

    private CancellationTokenSource LinkedTokenSource { get; set; }

    private Activity sessionActivity;

    public Spectator(
        ILogger<Spectator> logger,
        AppSettings settings,
        IReplayContext sessionHolder,
        IGameController controller,
        ITalentNotifier talentsNotifier,
        CancellationTokenProvider tokenProvider,
        SpectatorStatusStore statusStore,
        IObserverPanelRequests panelRequests,
        IObsController obsController,
        IGameTimer gameTimer,
        GameTimerLog clockLog,
        RecordingClock recordingClock
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.context = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.talentsNotifier =
            talentsNotifier ?? throw new ArgumentNullException(nameof(talentsNotifier));
        consoleTokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.panelRequests =
            panelRequests ?? throw new ArgumentNullException(nameof(panelRequests));
        this.obsController =
            obsController ?? throw new ArgumentNullException(nameof(obsController));
        this.gameTimer = gameTimer ?? throw new ArgumentNullException(nameof(gameTimer));
        this.clockLog = clockLog ?? throw new ArgumentNullException(nameof(clockLog));
        this.recordingClock =
            recordingClock ?? throw new ArgumentNullException(nameof(recordingClock));

        panelTimes = new()
        {
            { Panel.Talents, settings.PanelTimes.Talents },
            { Panel.DeathDamageRole, settings.PanelTimes.DeathDamageRole },
            { Panel.KillsDeathsAssists, settings.PanelTimes.KillsDeathsAssists },
            { Panel.Experience, settings.PanelTimes.Experience },
            { Panel.CarriedObjectives, settings.PanelTimes.CarriedObjectives },
            { Panel.None, TimeSpan.Zero },
        };
    }

    private TimeSpan SessionEndTime
    {
        get
        {
            if (Data == null)
            {
                return TimeSpan.Zero;
            }

            return ReplayAnalyzer.GetWatchUntil(
                Data.CoreKilled,
                Data.SessionEnd,
                settings.Spectate.EndScreenTime,
                Data.LoadedReplay?.Replay?.ReplayLength ?? TimeSpan.Zero
            );
        }
    }

    public void RecordHold(ClientHoldReason hold)
    {
        outcome = MatchCompletion.FromHold(hold);
    }

    public async Task SpectateAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.session");
        sessionActivity = activity;
        HeroesReplayTelemetry.TagReplay(
            activity,
            Data?.LoadedReplay?.FileInfo?.FullName,
            Data?.LoadedReplay?.Replay?.Map,
            Data?.LoadedReplay?.ReplayId,
            Data?.LoadedReplay?.Replay?.ReplayVersion
        );
        State = State.Loading;
        Timer = default;
        sessionStarted = DateTimeOffset.UtcNow;
        matchClockSeen = false;
        outcome = MatchOutcome.None;
        completion.Reset();
        gameTimer.Reset();
        endScreenStarted = null;
        lastAdvancedHud = TimeSpan.MinValue;
        lastAdvancedHudAt = default;
        nextEndScreenProbe = default;
        endScreenSeen = false;
        hungChecks = 0;
        missingProcessChecks = 0;
        PublishStatus();

        using (CancelSessionSource = new CancellationTokenSource())
        {
            using (
                LinkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    CancelSessionSource.Token,
                    consoleTokenProvider.Token
                )
            )
            {
                try
                {
                    await Task.WhenAll(
                            Task.Run(PanelLoopAsync, LinkedTokenSource.Token),
                            Task.Run(FocusLoopAsync, LinkedTokenSource.Token),
                            Task.Run(TalentsLoopAsync, LinkedTokenSource.Token),
                            Task.Run(StateLoopAsync, LinkedTokenSource.Token)
                        )
                        .ConfigureAwait(false);
                }
                finally
                {
                    PublishMatchCompleted();
                }
            }
        }
    }

    private async Task TalentsLoopAsync()
    {
        if (settings.TwitchExtension?.Enabled != true)
        {
            return;
        }

        talentsNotifier.ClearSession();
        try
        {
            while (!LinkedTokenSource.IsCancellationRequested)
            {
                try
                {
                    await talentsNotifier
                        .SendCurrentTalentsAsync(
                            Timer,
                            State == State.TimerDetected,
                            CancelSessionSource.Token
                        )
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(1), consoleTokenProvider.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete Heroes Profile Talents loop");
                }
            }
        }
        finally
        {
            try
            {
                await talentsNotifier.EndGameAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not close the Heroes Profile Twitch extension game");
            }
        }
    }

    private async Task StateLoopAsync()
    {
        while (!LinkedTokenSource.IsCancellationRequested)
        {
            try
            {
                GameTimerReading reading = await gameTimer
                    .ReadAsync(LinkedTokenSource.Token)
                    .ConfigureAwait(false);
                clockLog.Write(sessionActivity, reading);
                bool clockRead = reading.Ok && reading.Time.HasValue;

                bool clockAdvanced = clockRead && reading.Time.Value > lastAdvancedHud;
                if (clockRead)
                {
                    matchClockSeen = true;
                    Timer = reading.Time.Value;
                    recordingClock.Observe(reading.Time.Value);
                    if (clockAdvanced)
                    {
                        lastAdvancedHud = reading.Time.Value;
                        lastAdvancedHudAt = DateTimeOffset.UtcNow;
                        ServiceHeartbeat.RecordWork();
                    }
                }
                else if (State != State.TimerDetected)
                {
                    if (DateTimeOffset.UtcNow - lastClockMissLog > TimeSpan.FromSeconds(10))
                    {
                        lastClockMissLog = DateTimeOffset.UtcNow;
                        logger.LogInformation(
                            "Match clock has not started ({ClockReason}). No hero is selected until the memory clock ticks.",
                            reading.Reason
                        );
                    }

                    // The award screen has no running clock. A match can reach it without a
                    // single clock read (an unsupported build), so look for MVP before the first lock.
                    if (
                        MatchRecording.ShouldStopLoading(
                            matchClockSeen,
                            DateTimeOffset.UtcNow - sessionStarted
                        )
                    )
                    {
                        logger.LogWarning(
                            "No match clock after {Minutes:0} minutes. Ending this session. It will not be uploaded.",
                            MatchRecording.LoadingLimit.TotalMinutes
                        );
                        EndSession(MatchOutcome.LoadTimedOut);
                    }
                    else if (controller.IsGameRunning())
                    {
                        await TryOpenUnstartedReplayAsync().ConfigureAwait(false);
                        await ProbeEndScreenAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    await ProbeEndScreenAsync().ConfigureAwait(false);
                }

                bool firstTimer = State != State.TimerDetected && clockRead;
                State =
                    CancelSessionSource.IsCancellationRequested ? State.EndDetected
                    : clockRead || State == State.TimerDetected ? State.TimerDetected
                    : State.Loading;

                if (clockRead)
                {
                    logger.LogInformation("{State}, HUD Time: {Timer}", State, Timer);
                    context.Current.Timer = Timer;
                    if (State == State.TimerDetected || firstTimer)
                    {
                        obsController.UpdateReplayInfoVisibility(Timer);
                    }

                    if (firstTimer)
                    {
                        using Activity detected = HeroesReplayTelemetry.StartSpan(
                            "heroesreplay.timer.detected",
                            sessionActivity
                        );
                        detected?.SetTag("timer.source", reading.Source);
                        detected?.SetTag("timer.replay", Timer.ToString());
                        if (settings.OBS.Enabled)
                        {
                            logger.LogInformation("OBS game-scene (timer detected).");
                            obsController.SwapToGameScene();
                        }

                        BeginMatchRecording();
                        recordingClock.Observe(Timer);

                        if (
                            context.Current?.LoadedReplay?.RewardQueueItem?.Request?.PlayerIndex
                            is int
                        )
                        {
                            controller.ShowSelectedUnit();
                        }
                    }
                }

                if (!controller.IsGameRunning())
                {
                    missingProcessChecks++;
                    logger.LogWarning(
                        "Heroes of the Storm process is gone ({Count}).",
                        missingProcessChecks
                    );
                    if (missingProcessChecks >= 2)
                    {
                        logger.LogError("Game process exited (crash or closed); ending session.");
                        EndSession(MatchOutcome.ClientCrashed);
                    }
                }
                else if (SessionWatch.IsHung(controller.IsGameHung(), clockAdvanced))
                {
                    missingProcessChecks = 0;
                    hungChecks++;
                    logger.LogWarning("Game window hung ({Count}).", hungChecks);
                    if (hungChecks >= 45)
                    {
                        logger.LogError("Game not responding; ending session.");
                        EndSession(MatchOutcome.ClientHung);
                    }
                }
                else
                {
                    missingProcessChecks = 0;
                    hungChecks = 0;
                }

                TryEndAfterCore(clockRead);

                PublishStatus();

                await Task.Delay(TimeSpan.FromSeconds(1), LinkedTokenSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                logger.LogError(e, "Could not complete state loop");
            }
        }
    }

    private async Task TryOpenUnstartedReplayAsync()
    {
        if (matchClockSeen || controller.ReplayFileOpened || !controller.IsGameRunning())
        {
            return;
        }

        string path = Data?.LoadedReplay?.FileInfo?.FullName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await controller.OpenReplayFromHomeScreenAsync(path).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not open the replay from the home screen.");
        }
    }

    private void BeginMatchRecording()
    {
        if (!MatchRecording.ShouldStart(recordingClock.IsRunning, matchVisible: true))
        {
            return;
        }

        if (!SessionMedia.ShouldRecord(settings.OBS, Data?.LoadedReplay))
        {
            return;
        }

        ObsRecordingResult started = obsController.StartRecording();
        statusStore.Patch(status => ObsStatus.CopyRecording(status, started));
        if (started == null || !started.Owned)
        {
            logger.LogWarning(
                "OBS recording for replay {ReplayId} did not start ({Failure}). {Detail}",
                Data?.LoadedReplay?.ReplayId,
                started?.Failure,
                started?.Detail
            );
            return;
        }

        recordingClock.Start();
        logger.LogInformation(
            "OBS recording starts at the match clock for replay {ReplayId}.",
            Data?.LoadedReplay?.ReplayId
        );
    }

    private async Task ProbeEndScreenAsync()
    {
        if (endScreenSeen || DateTimeOffset.UtcNow < nextEndScreenProbe)
        {
            return;
        }

        if (
            lastAdvancedHudAt != default
            && DateTimeOffset.UtcNow - lastAdvancedHudAt < TimeSpan.FromSeconds(20)
        )
        {
            return;
        }

        nextEndScreenProbe = DateTimeOffset.UtcNow.AddSeconds(12);
        TimeSpan core = Data?.CoreKilled ?? TimeSpan.Zero;
        // MVP is the award screen. A camp tooltip can say "defeat" much earlier, so that
        // word still has to be near the parsed core. The clock may already be gone.
        bool nearCore = core <= TimeSpan.Zero || Timer + TimeSpan.FromMinutes(3) >= core;
        if (await controller.TrySeeEndScreenAsync(nearCore).ConfigureAwait(false))
        {
            endScreenSeen = true;
            logger.LogInformation(
                "MVP/victory screen detected at HUD {Timer}; holding for votes.",
                Timer
            );
        }
    }

    private void TryEndAfterCore(bool clockRead)
    {
        if (Data?.CoreKilled <= TimeSpan.Zero)
        {
            return;
        }

        bool nearCore = Timer + TimeSpan.FromSeconds(20) >= Data.CoreKilled;
        bool nearEnd = Timer + TimeSpan.FromMinutes(3) >= Data.CoreKilled;
        bool hudFrozen =
            State == State.TimerDetected
            && nearEnd
            && lastAdvancedHud >= TimeSpan.FromMinutes(2)
            && lastAdvancedHudAt != default
            && DateTimeOffset.UtcNow - lastAdvancedHudAt >= TimeSpan.FromSeconds(90);
        bool pastCore =
            Timer >= Data.CoreKilled || (!clockRead && nearCore) || hudFrozen || endScreenSeen;

        if (!pastCore)
        {
            endScreenStarted = null;
            return;
        }

        // The match clock stopping is the START of victory/MVP/votes, not the end.
        endScreenStarted ??= DateTimeOffset.UtcNow;
        TimeSpan held = DateTimeOffset.UtcNow - endScreenStarted.Value;
        TimeSpan need = EndScreenHold.Duration(endScreenSeen, settings.Spectate.EndScreenTime);

        if (clockRead)
        {
            logger.LogDebug(
                "Past core {CoreKilled}; clock still running at {Timer}, held {Held}.",
                Data.CoreKilled,
                Timer,
                held
            );
        }

        if (held >= need)
        {
            logger.LogInformation(
                "Ending session after {Held} of end screen (core {CoreKilled}, tracker {TrackerEnd}, timer {Timer}, clockRunning={ClockRunning}).",
                held,
                Data.CoreKilled,
                Data.SessionEnd,
                Timer,
                clockRead
            );
            EndSession(matchClockSeen ? MatchOutcome.VerifiedCompleted : MatchOutcome.Canceled);
        }
    }

    private void EndSession(MatchOutcome reason)
    {
        if (outcome == MatchOutcome.None)
        {
            outcome = reason;
        }

        CancelSessionSource.Cancel();
    }

    private async Task FocusLoopAsync()
    {
        int index = -1;

        while (!LinkedTokenSource.IsCancellationRequested)
        {
            try
            {
                if (
                    State == State.TimerDetected
                    && Data.TryGetFocus(Timer, out Focus focus)
                    && focus.Index != index
                )
                {
                    index = focus.Index;
                    logger.LogInformation(
                        "Selecting {Hero} slot {Index}. {Description}",
                        focus.Target.Character,
                        focus.Index,
                        focus.Description
                    );
                    using Activity swap = HeroesReplayTelemetry.StartSpan(
                        "heroesreplay.focus.swap",
                        sessionActivity
                    );
                    swap?.SetTag("focus.index", focus.Index);
                    swap?.SetTag("focus.hero", focus.Target?.Character);
                    swap?.SetTag("focus.player", focus.Target?.Name);
                    swap?.SetTag("focus.calculator", focus.Calculator?.Name);
                    swap?.SetTag("focus.points", focus.Points);
                    swap?.SetTag("focus.description", focus.Description);
                    swap?.SetTag("timer.replay", Timer.ToString());
                    controller.SendFocus(focus.Index);
                    statusStore.Patch(status =>
                    {
                        status.Focus = new SpectatorFocusStatus
                        {
                            Index = focus.Index,
                            Hero = focus.Target?.Character,
                            Player = focus.Target?.Name,
                            Calculator = focus.Calculator?.Name,
                            Points = focus.Points,
                            Description = focus.Description,
                        };
                    });
                }

                await Task.Delay(TimeSpan.FromSeconds(0.5), LinkedTokenSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                logger.LogError(e, "Could not complete focus loop");
            }
        }
    }

    private async Task PanelLoopAsync()
    {
        Panel current = Panel.None;
        TimeSpan second = TimeSpan.FromSeconds(1);
        TimeSpan timeShown = TimeSpan.Zero;
        bool visible = false;
        bool chatRequested = false;

        while (!LinkedTokenSource.IsCancellationRequested)
        {
            if (State != State.TimerDetected)
            {
                await Task.Delay(second).ConfigureAwait(false);
                continue;
            }

            try
            {
                Panel next = ChoosePanel(current, visible, ref chatRequested);

                TimeSpan shownLimit = chatRequested
                    ? panelRequests.ShowDuration
                    : panelTimes.GetValueOrDefault(current, TimeSpan.FromSeconds(30));

                if (
                    visible
                    && current != Panel.None
                    && (next != current || timeShown >= shownLimit)
                )
                {
                    using Activity hide = HeroesReplayTelemetry.StartSpan(
                        "heroesreplay.panel.hide",
                        sessionActivity
                    );
                    hide?.SetTag("panel", current.ToString());
                    hide?.SetTag("panel.chat_requested", chatRequested);
                    controller.SendPanel(current);
                    if (chatRequested)
                    {
                        panelRequests.MarkHidden(current);
                        chatRequested = false;
                    }

                    visible = false;
                    timeShown = TimeSpan.Zero;
                    if (next == current)
                    {
                        current = Panel.None;
                    }
                }

                if (!visible && next != Panel.None)
                {
                    using Activity show = HeroesReplayTelemetry.StartSpan(
                        "heroesreplay.panel.show",
                        sessionActivity
                    );
                    show?.SetTag("panel", next.ToString());
                    show?.SetTag("panel.chat_requested", chatRequested);
                    controller.SendPanel(next);
                    visible = true;
                    timeShown = TimeSpan.Zero;
                    current = next;
                }

                if (visible)
                {
                    timeShown = timeShown.Add(second);
                }

                await Task.Delay(second).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                logger.LogError(e, "Could not complete panel loop");
            }
        }
    }

    private Panel ChoosePanel(Panel current, bool visible, ref bool chatRequested)
    {
        if (panelRequests.TryConsume(out Panel requested, out string requestedBy))
        {
            logger.LogInformation(
                "Showing {Panel} for {Duration}s ({User}).",
                requested,
                (int)panelRequests.ShowDuration.TotalSeconds,
                requestedBy
            );
            chatRequested = true;
            return requested;
        }

        if (visible && chatRequested && current is Panel.DeathDamageRole or Panel.Talents)
        {
            return current;
        }

        if (!panelRequests.AutomaticTalentsEnabled)
        {
            return Panel.None;
        }

        if (Timer < settings.Spectate.TalentsPanelStartTime)
        {
            return Panel.Talents;
        }

        TimeSpan hold =
            settings.Spectate.TalentPanelHold > TimeSpan.Zero
                ? settings.Spectate.TalentPanelHold
                : TimeSpan.FromSeconds(8);
        TimeSpan cluster =
            settings.Spectate.TalentPanelCluster > TimeSpan.Zero
                ? settings.Spectate.TalentPanelCluster
                : TimeSpan.FromSeconds(15);
        if (TalentPanelSchedule.ShouldShow(Data?.TalentTimes, Timer, hold, cluster))
        {
            return Panel.Talents;
        }

        return Panel.None;
    }

    private void PublishStatus()
    {
        ContextData data = Data;
        statusStore.Patch(status =>
        {
            status.SpectatorRunning = true;
            status.Phase = State.ToString();
            status.Timer = Timer == default ? null : Timer.ToString();
            status.GatesOpen = data?.GatesOpen.ToString();
            status.CoreKilled = data?.CoreKilled.ToString();
            status.SessionEnd = SessionEndTime > TimeSpan.Zero ? SessionEndTime.ToString() : null;
            status.Map = EnglishMapNames.Prefer(
                data?.LoadedReplay?.HeroesProfileReplay?.Map,
                data?.LoadedReplay?.Replay?.Map,
                data?.LoadedReplay?.Replay?.MapAlternativeName
            );
            status.ReplayPath = data?.LoadedReplay?.FileInfo?.FullName;
            status.ReplayVersion = data?.LoadedReplay?.Replay?.ReplayVersion;
            status.ReplayId = data?.LoadedReplay?.ReplayId;
            status.SuppressPredictions = ReplayRequestKind.ViewerEnteredReplayId(
                data?.LoadedReplay
            );
        });
    }

    private void PublishMatchCompleted()
    {
        outcome = MatchCompletion.Normalize(
            outcome,
            matchClockSeen,
            consoleTokenProvider.Token.IsCancellationRequested
        );
        sessionActivity?.SetTag("session.outcome", outcome.ToString());
        int? replayId = Data?.LoadedReplay?.ReplayId;
        int? winner =
            outcome == MatchOutcome.VerifiedCompleted
                ? TwitchMatchPredictionService.WinningTeam(Data?.LoadedReplay?.Replay)
                : null;
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Session outcome {Outcome} for replay {ReplayId}.",
            outcome,
            replayId
        );
        statusStore.Patch(status =>
        {
            status.SpectatorRunning = false;
            // EndDetected, with no completion fields, is how an interrupted session is abandoned.
            status.Phase = nameof(State.EndDetected);
            status.ObsSession = false;
            status.Focus = null;
            status.Outcome = outcome.ToString();
            completion.Apply(status, outcome, replayId, winner, completedAt);
            if (Data?.LoadedReplay == null)
            {
                status.ReplayId = replayId ?? status.ReplayId;
                return;
            }

            status.Map =
                EnglishMapNames.Prefer(
                    Data.LoadedReplay.HeroesProfileReplay?.Map,
                    Data.LoadedReplay.Replay?.Map,
                    Data.LoadedReplay.Replay?.MapAlternativeName
                ) ?? status.Map;
            status.ReplayPath = Data.LoadedReplay.FileInfo?.FullName ?? status.ReplayPath;
            status.ReplayVersion = Data.LoadedReplay.Replay?.ReplayVersion ?? status.ReplayVersion;
            status.ReplayId = Data.LoadedReplay.ReplayId ?? status.ReplayId;
            status.SuppressPredictions = ReplayRequestKind.ViewerEnteredReplayId(Data.LoadedReplay);
        });
    }
}
