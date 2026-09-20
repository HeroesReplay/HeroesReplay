using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

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
    private readonly IMatchPredictionService predictions;
    private readonly Dictionary<Panel, TimeSpan> panelTimes;

    private State State { get; set; }

    private TimeSpan Timer { get; set; }

    private Stopwatch softwareClock;

    private bool replayViewConfigured;

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
        IMatchPredictionService predictions
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
        this.predictions = predictions ?? throw new ArgumentNullException(nameof(predictions));

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

            TimeSpan marker = Data.SessionEnd > TimeSpan.Zero ? Data.SessionEnd : Data.CoreKilled;
            if (marker <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            TimeSpan withHold = marker + settings.Spectate.EndScreenTime;
            TimeSpan length = Data.LoadedReplay?.Replay?.ReplayLength ?? TimeSpan.Zero;
            if (length > TimeSpan.Zero && withHold > length)
            {
                return length;
            }

            return withHold;
        }
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
        replayViewConfigured = false;
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
                    statusStore.MarkIdle("EndDetected");
                }
            }
        }
    }

    private async Task TalentsLoopAsync()
    {
        if (settings.TwitchExtension.Enabled)
        {
            talentsNotifier.ClearSession();

            while (!LinkedTokenSource.IsCancellationRequested)
            {
                try
                {
                    await talentsNotifier
                        .SendCurrentTalentsAsync(Timer, CancelSessionSource.Token)
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
    }

    private async Task StateLoopAsync()
    {
        while (!LinkedTokenSource.IsCancellationRequested)
        {
            try
            {
                TimeSpan? result;
                bool fromOcr = false;
                TimeSpan? ocr = await controller.TryGetTimerAsync().ConfigureAwait(false);
                if (ocr.HasValue)
                {
                    fromOcr = true;
                    result = ocr;
                    softwareClock = null;
                }
                else
                {
                    TimeSpan? sinceOpen = controller.ReplayOpenElapsed;
                    if (sinceOpen.HasValue)
                    {
                        result = sinceOpen;
                    }
                    else
                    {
                        softwareClock ??= Stopwatch.StartNew();
                        result = TimeSpan.FromSeconds(
                            Math.Floor(softwareClock.Elapsed.TotalSeconds)
                        );
                    }

                    if (State != State.TimerDetected)
                    {
                        logger.LogWarning(
                            "Timer OCR unavailable; using elapsed since the replay was opened ({Elapsed}).",
                            result
                        );
                    }
                }

                bool firstTimer = State != State.TimerDetected && result.HasValue;
                State =
                    CancelSessionSource.IsCancellationRequested ? State.EndDetected
                    : result.HasValue ? State.TimerDetected
                    : State.Loading;

                if (result.HasValue)
                {
                    // OCR reads the in-game clock (0:00 at gates). CoreKilled is replay time.
                    // The software clock starts at spectate/replay 0:00 — do not add GatesOpen again.
                    Timer = fromOcr ? result.Value.Add(context.Current.GatesOpen) : result.Value;
                    logger.LogInformation(
                        "{State}, {Source} {Raw} Replay Time: {Timer}",
                        State,
                        fromOcr ? "UI Time:" : "software:",
                        result.Value,
                        Timer
                    );
                    context.Current.Timer = Timer;

                    if (firstTimer)
                    {
                        using Activity detected = HeroesReplayTelemetry.StartSpan(
                            "heroesreplay.timer.detected",
                            sessionActivity
                        );
                        detected?.SetTag("timer.source", fromOcr ? "ocr" : "software");
                        detected?.SetTag("timer.raw", result.Value.ToString());
                        detected?.SetTag("timer.replay", Timer.ToString());
                        try
                        {
                            await predictions
                                .StartAsync(Data?.LoadedReplay, LinkedTokenSource.Token)
                                .ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "Could not open Twitch Blue/Red prediction.");
                        }
                    }

                    if (!replayViewConfigured)
                    {
                        using Activity view = HeroesReplayTelemetry.StartSpan(
                            "heroesreplay.view.configure",
                            sessionActivity
                        );
                        controller.HideReplayTimeline();
                        controller.ZoomOut();
                        replayViewConfigured = true;
                    }

                    TimeSpan sessionEnd = SessionEndTime;
                    if (sessionEnd > TimeSpan.Zero && Timer >= sessionEnd)
                    {
                        logger.LogInformation(
                            "Ending session at {SessionEnd} (core {CoreKilled}, tracker {TrackerEnd}, timer {Timer}).",
                            sessionEnd,
                            context.Current.CoreKilled,
                            context.Current.SessionEnd,
                            Timer
                        );
                        CancelSessionSource.Cancel();
                    }
                }

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

        if (Timer < settings.Spectate.TalentsPanelStartTime)
        {
            return Panel.Talents;
        }

        if (Data.Panels.TryGetValue(Timer, out Panel timed) && timed == Panel.Talents)
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
            status.Map = data?.LoadedReplay?.Replay?.Map;
            status.ReplayPath = data?.LoadedReplay?.FileInfo?.FullName;
            status.ReplayVersion = data?.LoadedReplay?.Replay?.ReplayVersion;
            status.ReplayId = data?.LoadedReplay?.ReplayId;
        });
    }
}
