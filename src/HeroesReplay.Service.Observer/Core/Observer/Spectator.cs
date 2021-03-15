using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Analyzer;
using HeroesReplay.Service.Spectator.Core.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public class Spectator : ISpectator
    {
        private readonly IGameController controller;
        private readonly ITalentNotifier talentsNotifier;
        private readonly ILogger<Spectator> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly IReplayContext context;
        private readonly CancellationTokenSource cts;
        private readonly Dictionary<Panel, TimeSpan> panelTimes;

        private State State { get; set; }

        private TimeSpan Timer { get; set; }

        private ContextData Data => context.Current;

        private CancellationTokenSource CancelSessionSource { get; set; }

        private CancellationTokenSource LinkedTokenSource { get; set; }

        public Spectator(ILogger<Spectator> logger, IOptions<AppSettings> settings, IReplayContext sessionHolder, IGameController controller, ITalentNotifier talentsNotifier, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.context = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.talentsNotifier = talentsNotifier ?? throw new ArgumentNullException(nameof(talentsNotifier));
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));

            panelTimes = new()
            {
                { Panel.Talents, settings.Value.PanelTimes.Talents },
                { Panel.DeathDamageRole, settings.Value.PanelTimes.DeathDamageRole },
                { Panel.KillsDeathsAssists, settings.Value.PanelTimes.KillsDeathsAssists },
                { Panel.Experience, settings.Value.PanelTimes.Experience },
                { Panel.CarriedObjectives, settings.Value.PanelTimes.CarriedObjectives },
                { Panel.None, TimeSpan.Zero }
            };
        }

        public async Task SpectateAsync()
        {
            State = State.Loading;
            Timer = default;

            using (CancelSessionSource = new CancellationTokenSource())
            {
                using (LinkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancelSessionSource.Token, cts.Token))
                {
                    await Task.WhenAll(
                        Task.Run(PanelLoopAsync, LinkedTokenSource.Token),
                        Task.Run(FocusLoopAsync, LinkedTokenSource.Token),
                        Task.Run(TalentsLoopAsync, LinkedTokenSource.Token),
                        Task.Run(StateLoopAsync, LinkedTokenSource.Token)).ConfigureAwait(false);
                }
            }
        }

        private async Task TalentsLoopAsync()
        {
            if (settings.Value.TwitchExtension.Enabled)
            {
                talentsNotifier.ClearSession();

                while (!LinkedTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        await talentsNotifier.SendCurrentTalentsAsync(Timer, CancelSessionSource.Token).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {

                    }
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
                    TimeSpan? result = await TryGetOcrTimer().ConfigureAwait(false);

                    State = CancelSessionSource.IsCancellationRequested ? State.EndDetected : result.HasValue ? State.TimerDetected : State.Loading;

                    if (result.HasValue)
                    {
                        Timer = result.Value.Add(context.Current.GatesOpen);
                        logger.LogInformation($"{State}, UI Time: {result.Value} Replay Time: {Timer}");
                        context.Current.Timer = Timer;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {

                }
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
                    if (State == State.TimerDetected && Data.Players.TryGetValue(Timer, out Focus focus) && focus != null && focus.Index != index)
                    {
                        index = focus.Index;
                        logger.LogDebug($"Selecting {focus.Target.Character}. Description: {focus.Description}");
                        controller.SendFocus(focus.Index);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete focus loop");
                }
            }
        }

        private async Task PanelLoopAsync()
        {
            Panel current = Panel.None;
            Panel next = Panel.None;

            TimeSpan second = TimeSpan.FromSeconds(1);
            TimeSpan timeHidden = TimeSpan.Zero;
            TimeSpan timeShown = TimeSpan.Zero;

            bool visible = true;

            while (!LinkedTokenSource.IsCancellationRequested)
            {
                if (State == State.TimerDetected)
                {
                    try
                    {
                        if (Timer < settings.Value.Spectate.TalentsPanelStartTime)
                        {
                            next = Panel.Talents;
                        }
                        else if (Data.Panels.TryGetValue(Timer, out Panel panel) && panel != current)
                        {
                            logger.LogDebug($"Data panels timer match found at: {Timer}");

                            if (current != Panel.Talents)
                                next = panel; // It's not important enough to show KDA over Talents
                        }
                        else if (timeHidden >= settings.Value.Spectate.PanelDownTime)
                        {
                            next = GetNextPanel(current);
                        }

                        bool shouldHide = Timer > settings.Value.Spectate.TalentsPanelStartTime &&
                                          current != Panel.None &&
                                          timeShown >= panelTimes[current];

                        if (shouldHide)
                        {
                            controller.SendPanel((int)current);
                            visible = false;
                            timeHidden = TimeSpan.Zero;
                            timeShown = TimeSpan.Zero;
                        }

                        bool shouldShow = current == Panel.None ||
                                          timeHidden >= settings.Value.Spectate.PanelDownTime ||
                                          next != current;

                        if (shouldShow)
                        {
                            controller.SendPanel((int)next);
                            visible = true;
                            timeShown = TimeSpan.Zero;
                            timeHidden = TimeSpan.Zero;
                            current = next;
                        }

                        timeShown = timeShown.Add(visible ? second : TimeSpan.Zero);
                        timeHidden = timeHidden.Add(visible ? TimeSpan.Zero : second);

                        logger.LogDebug($"{Enum.GetName(typeof(Panel), current)}" + (visible ? $" shown for: {timeShown}" : $" hidden for: {timeHidden}"));

                        await Task.Delay(second).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {

                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Could not complete panel loop");
                    }
                }
            }
        }

        private Panel GetNextPanel(Panel current) => current switch
        {
            Panel.None => Panel.Talents,
            Panel.Talents => Panel.DeathDamageRole,
            Panel.KillsDeathsAssists => Data.IsCarriedObjectiveMap ? Panel.CarriedObjectives : Panel.DeathDamageRole,
            Panel.CarriedObjectives => Panel.DeathDamageRole,
            Panel.DeathDamageRole => Panel.Experience,
            Panel.Experience => Panel.Talents,
            _ => Panel.Talents,
        };

        const string StateKey = "State";

        private async Task<TimeSpan?> TryGetOcrTimer()
        {
            return await Policy
                .HandleResult<TimeSpan?>(result => result == null)
                .WaitAndRetryAsync(
                        retryCount: settings.Value.Spectate.RetryTimerCountBeforeForceEnd,
                        sleepDurationProvider: (retry, context) => settings.Value.Spectate.RetryTimerSleepDuration,
                        onRetry: OnRetry)
                .ExecuteAsync((context, token) => controller.TryGetTimerAsync(), new Polly.Context("Timer") { { StateKey, State } }, LinkedTokenSource.Token).ConfigureAwait(false);
        }

        private void OnRetry(DelegateResult<TimeSpan?> outcome, TimeSpan duration, int retryCount, Polly.Context context)
        {
            var state = (State)context[StateKey];
            var isMax = retryCount >= settings.Value.Spectate.RetryTimerCountBeforeForceEnd;
            var isTimerNotFound = outcome.Result == null;

            if (state == State.Loading)
            {
                logger.LogInformation($"Waiting for timer...attempt {retryCount}.");
            }
            else if (state == State.TimerDetected && isTimerNotFound && isMax)
            {
                logger.LogInformation($"Timer could not be found after {retryCount}. Sending session cancellation.");
                CancelSessionSource.Cancel();
            }
            else
            {
                logger.LogWarning($"Timer failed. Waiting {duration} before next retry. Retry attempt {retryCount}");
            }
        }
    }
}