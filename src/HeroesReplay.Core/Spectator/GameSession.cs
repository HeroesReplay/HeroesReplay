using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameSession : IGameSession
    {
        private readonly IGameController controller;
        private readonly ILogger<GameSession> logger;
        private readonly AppSettings settings;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ISessionHolder sessionHolder;

        private CancellationToken Token => tokenProvider.Token;

        private State State { get; set; }

        private TimeSpan Timer { get; set; }

        private SessionData Data => sessionHolder.SessionData;

        public GameSession(ILogger<GameSession> logger, AppSettings settings, ISessionHolder sessionHolder, IGameController controller, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public async Task SpectateAsync()
        {
            State = State.Loading;
            Timer = default;

            await Task.WhenAll(
                Task.Run(PanelLoopAsync, Token),
                Task.Run(FocusLoopAsync, Token),
                Task.Run(StateLoopAsync, Token)).ConfigureAwait(false);
        }

        private async Task StateLoopAsync()
        {
            while (State != State.EndDetected)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    (TimeSpan? Timer, bool EndDetected) result = await TryGetOcrTimer().ConfigureAwait(false);

                    State = result.EndDetected ? State.EndDetected : result.Timer.HasValue ? State.TimerDetected : State.Loading;
                    Timer = result.Timer ?? Timer;

                    if (result.Timer.HasValue)
                    {
                        TimeSpan UITimer = Timer.AddNegativeOffset(settings.Spectate.GameLoopsOffset, settings.Spectate.GameLoopsPerSecond);
                        logger.LogInformation($"{State}, UI Time: {UITimer} Replay Time: {Timer}");
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
            Panel previous = Panel.None;
            Panel next = Panel.None;
            TimeSpan cooldown = settings.Spectate.PanelRotateTime;
            TimeSpan second = TimeSpan.FromSeconds(1);

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
                        else if (Data.Panels.TryGetValue(Timer, out Panel panel) && panel != previous)
                        {
                            logger.LogDebug($"Data panels timer match found at: {Timer}");
                            next = panel;
                        }
                        else if (cooldown <= TimeSpan.Zero)
                        {
                            next = previous switch
                            {
                                Panel.None => Panel.Talents,
                                Panel.KillsDeathsAssists => Panel.ActionsPerMinute,
                                Panel.CarriedObjectives => Panel.CrowdControlEnemyHeroes,
                                Panel.CrowdControlEnemyHeroes => Panel.DeathDamageRole,
                                Panel.DeathDamageRole => Panel.Experience,
                                Panel.Experience => Panel.Talents,
                                Panel.Talents => Panel.TimeDeadDeathsSelfSustain,
                                Panel.TimeDeadDeathsSelfSustain => Panel.KillsDeathsAssists,
                                Panel.ActionsPerMinute => Data.IsCarriedObjectiveMap ? Panel.CarriedObjectives : Panel.CrowdControlEnemyHeroes,
                                _ => Panel.Talents,
                            };
                        }

                        if (next != previous)
                        {
                            controller.SendPanel(next);
                            cooldown = settings.Spectate.PanelRotateTime;
                            previous = next;
                        }

                        cooldown = cooldown.Subtract(second);
                        await Task.Delay(second).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Could not complete panel loop");
                    }
                }
            }
        }

        private async Task<(TimeSpan? Timer, bool EndDetected)> TryGetOcrTimer()
        {
            const string EndDetectedKey = "EndDetected";
            const string StateKey = "State";

            var context = new Context("Timer")
            {
                { EndDetectedKey, false },
                { StateKey, State }
            };

            void onRetry(DelegateResult<TimeSpan?> outcome, TimeSpan duration, int retryCount, Context context)
            {
                var state = (State)context[StateKey];
                var isMax = retryCount >= settings.Spectate.RetryTimerCountBeforeForceEnd;
                var isTimerNotFound = outcome.Result == null;

                if (state == State.Loading)
                {
                    logger.LogWarning($"Timer could not be found after {retryCount}. Game still loading.");
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

            TimeSpan? timer = await Policy
                .HandleResult<TimeSpan?>(result => result == null)
                .WaitAndRetryAsync(retryCount: settings.Spectate.RetryTimerCountBeforeForceEnd, sleepDurationProvider: (retry, context) => settings.Spectate.RetryTimerSleepDuration, onRetry: onRetry)
                .ExecuteAsync((context, token) => controller.TryGetTimerAsync(), context, Token).ConfigureAwait(false);

            return (Timer: timer, EndDetected: (bool)context[EndDetectedKey]);
        }
    }
}