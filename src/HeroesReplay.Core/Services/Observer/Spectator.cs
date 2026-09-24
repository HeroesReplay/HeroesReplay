using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
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
    private readonly IObsController obsController;
    private readonly Dictionary<Panel, TimeSpan> panelTimes;

    private State State { get; set; }

    private TimeSpan Timer { get; set; }

    private readonly MemoryMatchClock memoryClock;

    private readonly IGameTimer gameTimer;

    private readonly GameTimerLog clockLog;

    private DateTimeOffset? endScreenStarted;

    private TimeSpan lastAdvancedHud = TimeSpan.MinValue;

    private DateTimeOffset lastAdvancedHudAt;

    private DateTimeOffset nextEndScreenProbe;
    private DateTimeOffset lastOcrMissLog;

    private bool endScreenSeen;

    private int hungChecks;

    private int missingProcessChecks;

    private readonly RecordingClock recordingClock = new();

    private IReadOnlyList<TeamKillClip> matchClips;

    private int writtenClips;

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
        GameTimerLog clockLog
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
        memoryClock = new MemoryMatchClock(logger);
        this.gameTimer = gameTimer ?? throw new ArgumentNullException(nameof(gameTimer));
        this.clockLog = clockLog ?? throw new ArgumentNullException(nameof(clockLog));

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
        gameTimer.Reset();
        memoryClock.Reset();
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

    private void ObserveMatchClips()
    {
        try
        {
            TimeSpan? elapsed = obsController.RecordingElapsed();
            if (elapsed == null || Data?.Directory == null)
            {
                return;
            }

            recordingClock.Observe(Timer, elapsed.Value);
            if (matchClips == null)
            {
                int window = (int)settings.Spectate.KillStreakWindow.TotalSeconds;
                IEnumerable<Heroes.ReplayParser.Unit> units =
                    Data.LoadedReplay?.Replay?.Players?.SelectMany(player =>
                        player.HeroUnits ?? new List<Heroes.ReplayParser.Unit>()
                    );
                matchClips = TeamKillClips.Select(
                    TeamKillDeaths.FromHeroUnits(units),
                    window,
                    leadSeconds: 12,
                    tailSeconds: 8
                );
            }

            IReadOnlyList<MatchClipEntry> ready = MatchClipList.Ready(
                Data.LoadedReplay?.ReplayId,
                matchClips,
                recordingClock
            );
            if (ready.Count <= writtenClips)
            {
                return;
            }

            writtenClips = ready.Count;
            MatchClipList.Write(
                Path.Combine(Data.Directory.FullName, MatchClipList.FileName),
                ready
            );
            logger.LogInformation("Wrote {Count} match clips to clips.json.", ready.Count);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not update clips.json.");
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
                reading = PreferLockedScan(reading);
                clockLog.Write(sessionActivity, reading);
                bool fromOcr = reading.Ok && reading.Time.HasValue;

                if (fromOcr)
                {
                    Timer = reading.Time.Value;
                    if (reading.Time.Value > lastAdvancedHud)
                    {
                        lastAdvancedHud = reading.Time.Value;
                        lastAdvancedHudAt = DateTimeOffset.UtcNow;
                    }

                    if (reading.Source != "memory")
                    {
                        ObserveMemoryTimer(reading.Time.Value);
                    }
                }
                else if (State != State.TimerDetected)
                {
                    if (DateTimeOffset.UtcNow - lastOcrMissLog > TimeSpan.FromSeconds(10))
                    {
                        lastOcrMissLog = DateTimeOffset.UtcNow;
                        logger.LogWarning(
                            "Timer OCR missed. No hero is selected until the HUD clock reads MM:SS."
                        );
                    }

                    // The award screen has no clock. A match can reach it without a single
                    // MM:SS read if capture was denied, so look for MVP before the first lock.
                    if (controller.IsGameRunning())
                    {
                        await ProbeEndScreenAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    await ProbeEndScreenAsync().ConfigureAwait(false);
                }

                bool firstTimer = State != State.TimerDetected && fromOcr;
                State =
                    CancelSessionSource.IsCancellationRequested ? State.EndDetected
                    : fromOcr || State == State.TimerDetected ? State.TimerDetected
                    : State.Loading;

                if (fromOcr)
                {
                    logger.LogInformation("{State}, HUD Time: {Timer}", State, Timer);
                    context.Current.Timer = Timer;
                    ObserveMatchClips();

                    if (firstTimer)
                    {
                        using Activity detected = HeroesReplayTelemetry.StartSpan(
                            "heroesreplay.timer.detected",
                            sessionActivity
                        );
                        detected?.SetTag("timer.source", "ocr");
                        detected?.SetTag("timer.replay", Timer.ToString());
                        if (settings.OBS.Enabled)
                        {
                            logger.LogInformation("OBS game-scene (timer detected).");
                            obsController.SwapToGameScene();
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
                        CancelSessionSource.Cancel();
                    }
                }
                else if (controller.IsGameHung())
                {
                    missingProcessChecks = 0;
                    hungChecks++;
                    logger.LogWarning("Game window hung ({Count}).", hungChecks);
                    if (hungChecks >= 3)
                    {
                        logger.LogError("Game not responding; ending session.");
                        CancelSessionSource.Cancel();
                    }
                }
                else
                {
                    missingProcessChecks = 0;
                    hungChecks = 0;
                }

                TryEndAfterCore(fromOcr);

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

    private GameTimerReading PreferLockedScan(GameTimerReading reading)
    {
        if (reading.Ok || !settings.Spectate.UseMemoryTimer || !memoryClock.IsLocked)
        {
            return reading;
        }

        if (memoryClock.LastRead is not TimeSpan ui)
        {
            return reading;
        }

        TimeSpan gates = Data?.GatesOpen ?? TimeSpan.Zero;
        TimeSpan replayTime;
        try
        {
            replayTime = ui + gates;
        }
        catch (OverflowException)
        {
            return reading;
        }

        if (replayTime < TimeSpan.FromMinutes(-3) || replayTime > TimeSpan.FromMinutes(90))
        {
            return reading;
        }

        return new GameTimerReading(true, "memory-scan", "locked", replayTime);
    }

    private void ObserveMemoryTimer(TimeSpan hudTime)
    {
        if (!settings.Spectate.MemoryTimerEnabled)
        {
            return;
        }

        Process process = controller.GetGameProcess();
        if (process == null)
        {
            return;
        }

        // The on-screen clock starts at 0:00 when gates open. Replay time is that
        // clock plus GatesOpen. The client stores the on-screen seconds.
        TimeSpan gates = Data?.GatesOpen ?? TimeSpan.Zero;
        TimeSpan uiTime = hudTime - gates;
        if (uiTime < TimeSpan.Zero)
        {
            uiTime = hudTime;
        }

        memoryClock.Observe(process, uiTime);
        if (memoryClock.LastRead != null || memoryClock.CandidateCount > 0)
        {
            TimeSpan? memoryReplay =
                memoryClock.LastRead == null ? null : memoryClock.LastRead + gates;
            logger.LogInformation(
                "HUD {Hud} ui={Ui} memory={Memory} asReplay={MemoryReplay} locked={Locked} phase={Phase} candidates={Candidates}",
                hudTime,
                uiTime,
                memoryClock.LastRead,
                memoryReplay,
                memoryClock.IsLocked,
                memoryClock.Phase,
                memoryClock.CandidateCount
            );
        }
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

    private void TryEndAfterCore(bool ocrTimerVisible)
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
            Timer >= Data.CoreKilled
            || (!ocrTimerVisible && nearCore)
            || hudFrozen
            || endScreenSeen;

        if (!pastCore)
        {
            endScreenStarted = null;
            return;
        }

        // HUD clock vanishing is the START of victory/MVP/votes, not the end.
        endScreenStarted ??= DateTimeOffset.UtcNow;
        TimeSpan held = DateTimeOffset.UtcNow - endScreenStarted.Value;
        TimeSpan need = EndScreenHold.Duration(endScreenSeen, settings.Spectate.EndScreenTime);

        if (ocrTimerVisible)
        {
            logger.LogDebug(
                "Past core {CoreKilled}; clock still visible at {Timer}, held {Held}.",
                Data.CoreKilled,
                Timer,
                held
            );
        }

        if (held >= need)
        {
            logger.LogInformation(
                "Ending session after {Held} of end screen (core {CoreKilled}, tracker {TrackerEnd}, timer {Timer}, clockVisible={ClockVisible}).",
                held,
                Data.CoreKilled,
                Data.SessionEnd,
                Timer,
                ocrTimerVisible
            );
            CancelSessionSource.Cancel();
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
        int? winner = TwitchMatchPredictionService.WinningTeam(Data?.LoadedReplay?.Replay);
        statusStore.Patch(status =>
        {
            status.SpectatorRunning = false;
            status.Phase = nameof(State.EndDetected);
            status.ObsSession = false;
            status.Focus = null;
            status.CompletedAt = DateTimeOffset.UtcNow;
            status.CompletedReplayId = Data?.LoadedReplay?.ReplayId;
            status.CompletedWinnerTeam = winner;
            if (Data?.LoadedReplay == null)
            {
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
