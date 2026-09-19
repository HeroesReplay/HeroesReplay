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
using Microsoft.Extensions.Logging;
using Polly;
using PollyContext = Polly.Context;

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
        IObserverPanelRequests panelRequests
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
            if (Data == null || Data.CoreKilled <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return Data.CoreKilled + settings.Spectate.EndScreenTime;
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
                if (softwareClock == null)
                {
                    result = await TryGetOcrTimer().ConfigureAwait(false);
                    if (!result.HasValue)
                    {
                        softwareClock = Stopwatch.StartNew();
                        result = TimeSpan.Zero;
                        logger.LogWarning(
                            "Timer OCR unavailable; spectating from a software clock starting at 0:00."
                        );
                    }
                }
                else
                {
                    result = TimeSpan.FromSeconds(Math.Floor(softwareClock.Elapsed.TotalSeconds));
                }

                bool firstTimer = State != State.TimerDetected && result.HasValue;
                State =
                    CancelSessionSource.IsCancellationRequested ? State.EndDetected
                    : result.HasValue ? State.TimerDetected
                    : State.Loading;

                if (result.HasValue)
                {
                    Timer = result.Value.Add(context.Current.GatesOpen);
                    logger.LogInformation($"{State}, UI Time: {result.Value} Replay Time: {Timer}");
                    context.Current.Timer = Timer;

                    if (firstTimer)
                    {
                        using Activity detected = HeroesReplayTelemetry.StartSpan(
                            "heroesreplay.timer.detected",
                            sessionActivity
                        );
                        detected?.SetTag(
                            "timer.source",
                            softwareClock == null ? "ocr" : "software"
                        );
                        detected?.SetTag("timer.ui", result.Value.ToString());
                        detected?.SetTag("timer.replay", Timer.ToString());
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
                            "Core destroyed at {CoreKilled}; end screen until {SessionEnd} (timer {Timer}).",
                            context.Current.CoreKilled,
                            sessionEnd,
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
                TimeSpan key = TimeSpan.FromSeconds(Math.Floor(Timer.TotalSeconds));
                if (
                    State == State.TimerDetected
                    && Data.Players.TryGetValue(key, out Focus focus)
                    && focus != null
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
            status.Map = data?.LoadedReplay?.Replay?.Map;
            status.ReplayPath = data?.LoadedReplay?.FileInfo?.FullName;
            status.ReplayVersion = data?.LoadedReplay?.Replay?.ReplayVersion;
            status.ReplayId = data?.LoadedReplay?.ReplayId;
        });
    }

    const string StateKey = "State";

    private async Task<TimeSpan?> TryGetOcrTimer()
    {
        return await Policy
            .HandleResult<TimeSpan?>(result => result == null)
            .WaitAndRetryAsync(
                retryCount: settings.Spectate.RetryTimerCountBeforeForceEnd,
                sleepDurationProvider: (retry, context) =>
                    settings.Spectate.RetryTimerSleepDuration,
                onRetry: OnRetry
            )
            .ExecuteAsync(
                (context, token) => controller.TryGetTimerAsync(),
                new PollyContext("Timer") { { StateKey, State } },
                LinkedTokenSource.Token
            )
            .ConfigureAwait(false);
    }

    private void OnRetry(
        DelegateResult<TimeSpan?> outcome,
        TimeSpan duration,
        int retryCount,
        PollyContext context
    )
    {
        var state = (State)context[StateKey];
        var isMax = retryCount >= settings.Spectate.RetryTimerCountBeforeForceEnd;
        var isTimerNotFound = outcome.Result == null;

        if (state == State.Loading)
        {
            logger.LogInformation($"Waiting for timer...attempt {retryCount}.");
        }
        else if (state == State.TimerDetected && isTimerNotFound && isMax)
        {
            bool pastEnd = SessionEndTime > TimeSpan.Zero && Timer >= SessionEndTime;
            if (pastEnd || Data.CoreKilled == TimeSpan.Zero)
            {
                logger.LogInformation(
                    $"Timer could not be found after {retryCount}. Sending session cancellation."
                );
                CancelSessionSource.Cancel();
            }
            else
            {
                logger.LogWarning(
                    "Timer OCR failed but replay end {CoreKilled} has not been reached (timer {Timer}).",
                    Data.CoreKilled,
                    Timer
                );
            }
        }
        else
        {
            logger.LogWarning(
                $"Timer failed. Waiting {duration} before next retry. Retry attempt {retryCount}"
            );
        }
    }
}
