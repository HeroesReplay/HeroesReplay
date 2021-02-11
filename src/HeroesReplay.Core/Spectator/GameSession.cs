using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameSession : IGameSession
    {
        private readonly IGameController controller;
        private readonly ITalentNotifier talentsNotifier;
        private readonly ILogger<GameSession> logger;
        private readonly AppSettings settings;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ISessionHolder sessionHolder;
        private readonly Dictionary<Panel, TimeSpan> panelTimes;

        private CancellationToken Token => tokenProvider.Token;

        private State State { get; set; }

        private TimeSpan Timer { get; set; }

        private SessionData Data => sessionHolder.SessionData;

        public GameSession(
            ILogger<GameSession> logger,
            AppSettings settings,
            ISessionHolder sessionHolder,
            IGameController controller,
            ITalentNotifier talentsNotifier,
            CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.talentsNotifier = talentsNotifier ?? throw new ArgumentNullException(nameof(talentsNotifier));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

            this.panelTimes = new()
            {
                { Panel.Talents, settings.PanelTimes.Talents },
                { Panel.DeathDamageRole, settings.PanelTimes.DeathDamageRole },
                { Panel.KillsDeathsAssists, settings.PanelTimes.KillsDeathsAssists },
                { Panel.Experience, settings.PanelTimes.Experience },
                { Panel.CarriedObjectives, settings.PanelTimes.CarriedObjectives },
                { Panel.None, TimeSpan.Zero }
            };
        }

        public async Task SpectateAsync()
        {
            State = State.Loading;
            Timer = default;

            await Task.WhenAll(
                Task.Run(PanelLoopAsync, Token),
                Task.Run(FocusLoopAsync, Token),
                Task.Run(TalentsLoopAsync, Token),
                Task.Run(StateLoopAsync, Token)).ConfigureAwait(false);
        }

        private async Task TalentsLoopAsync()
        {
            if (settings.TwitchExtension.Enabled)
            {
                while (State != State.EndDetected)
                {
                    Token.ThrowIfCancellationRequested();

                    try
                    {
                        await talentsNotifier.SendCurrentTalentsAsync(Timer).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Could not complete Heroes Profile Talents loop");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), Token).ConfigureAwait(false);
                }

                talentsNotifier.ClearSession();
            }
        }

        private async Task StateLoopAsync()
        {
            while (State != State.EndDetected)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    var result = await TryGetOcrTimer().ConfigureAwait(false);

                    State = result.EndDetected ? State.EndDetected : result.Timer.HasValue ? State.TimerDetected : State.Loading;

                    if (result.Timer.HasValue)
                    {
                        Timer = result.Timer.Value.Add(sessionHolder.SessionData.GatesOpen);
                        logger.LogInformation($"{State}, UI Time: {result.Timer.Value} Replay Time: {Timer}");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete state loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), Token).ConfigureAwait(false);
            }
        }

        private async Task FocusLoopAsync()
        {
            int index = -1;

            while (State != State.EndDetected)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    if (State == State.TimerDetected && Data.Players.TryGetValue(Timer, out Focus focus) && focus != null && focus.Index != index)
                    {
                        index = focus.Index;
                        logger.LogInformation($"Selecting {focus.Target.Character}. Description: {focus.Description}");
                        controller.SendFocus(focus.Index);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete focus loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
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

            while (State != State.EndDetected)
            {
                Token.ThrowIfCancellationRequested();

                if (State == State.TimerDetected)
                {
                    try
                    {
                        if (Timer < settings.Spectate.TalentsPanelStartTime)
                        {
                            next = Panel.Talents;
                        }
                        else if (Data.Panels.TryGetValue(Timer, out Panel panel) && panel != current)
                        {
                            logger.LogDebug($"Data panels timer match found at: {Timer}");

                            if (current != Panel.Talents)
                                next = panel; // It's not important enough to show KDA over Talents
                        }
                        else if (timeHidden >= settings.Spectate.PanelDownTime)
                        {
                            next = GetNextPanel(current);
                        }

                        bool shouldHide = Timer > settings.Spectate.TalentsPanelStartTime &&
                                          current != Panel.None &&
                                          timeShown >= panelTimes[current];

                        if (shouldHide)
                        {
                            controller.SendPanel(current);
                            visible = false;
                            timeHidden = TimeSpan.Zero;
                            timeShown = TimeSpan.Zero;
                        }

                        bool shouldShow = current == Panel.None ||
                                          timeHidden >= settings.Spectate.PanelDownTime ||
                                          next != current;

                        if (shouldShow)
                        {
                            controller.SendPanel(next);
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

        const string EndDetectedKey = "EndDetected";
        const string StateKey = "State";

        private async Task<(TimeSpan? Timer, bool EndDetected)> TryGetOcrTimer()
        {
            var context = new Context("Timer")
            {
                { EndDetectedKey, false },
                { StateKey, State }
            };

            TimeSpan? timer = await Policy
                .HandleResult<TimeSpan?>(result => result == null)
                .WaitAndRetryAsync(
                        retryCount: settings.Spectate.RetryTimerCountBeforeForceEnd,
                        sleepDurationProvider: (retry, context) => settings.Spectate.RetryTimerSleepDuration,
                        onRetry: OnRetry)
                .ExecuteAsync((context, token) => controller.TryGetTimerAsync(), context, Token).ConfigureAwait(false);

            return (Timer: timer, EndDetected: (bool)context[EndDetectedKey]);
        }

        private void OnRetry(DelegateResult<TimeSpan?> outcome, TimeSpan duration, int retryCount, Context context)
        {
            var state = (State)context[StateKey];
            var isMax = retryCount >= settings.Spectate.RetryTimerCountBeforeForceEnd;
            var isTimerNotFound = outcome.Result == null;

            if (state == State.Loading)
            {
                logger.LogWarning($"Waiting for timer...attempt {retryCount}.");
            }
            else if (state == State.TimerDetected && isTimerNotFound && isMax)
            {
                logger.LogWarning($"Timer could not be found after {retryCount}. Shutting down.");
                context[EndDetectedKey] = true;
            }
            else
            {
                logger.LogWarning($"Timer failed. Waiting {duration} before next retry. Retry attempt {retryCount}");
            }
        }
    }
}