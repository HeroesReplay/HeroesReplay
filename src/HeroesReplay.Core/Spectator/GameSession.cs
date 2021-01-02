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
            State = State.Start;
            Timer = default;

            await Task.WhenAll(
                Task.Run(PanelLoopAsync, Token),
                Task.Run(FocusLoopAsync, Token),
                Task.Run(StateLoopAsync, Token));

            logger.LogInformation($"Game has ended. Waiting for {settings.Spectate.EndScreenTime}");

            await Task.Delay(settings.Spectate.EndScreenTime);
        }

        private async Task StateLoopAsync()
        {
            while (State != State.End)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    (TimeSpan? timer, bool endDetected) = await TryGetOcrTimer();

                    if (endDetected)
                    {
                        State = State.End;
                    }
                    else if (timer.HasValue)
                    {
                        State = State.Running;
                        Timer = timer.Value;

                        TimeSpan UITimer = Timer.AddNegativeOffset(settings.Spectate.GameLoopsOffset, settings.Spectate.GameLoopsPerSecond);
                        logger.LogInformation($"{State}, UI: {UITimer} Actual: {Timer}");
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

            while (State != State.End)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    if (State == State.Running)
                    {
                        if (Data.Players.TryGetValue(Timer, out Focus? focus) && focus != null && focus.Index != index)
                        {
                            index = focus.Index;
                            logger.LogInformation($"Selecting {focus.Target.HeroId}. Description: {focus.Description}");
                            controller.SendFocus(focus.Index);
                        }
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

            while (State != State.End)
            {
                Token.ThrowIfCancellationRequested();

                if (State == State.Running)
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

        private async Task<(TimeSpan? Timer, bool ForceEnd)> TryGetOcrTimer()
        {
            var context = new Context("Timer");
            context.Add("ForceEnd", false);
            context.Add("State", State);
            context.Add("End", Data.End);

            TimeSpan? timer = await Policy
                .HandleResult<TimeSpan?>(result => result == null)
                .WaitAndRetryAsync(
                    retryCount: settings.Spectate.RetryTimerCountBeforeForceEnd,
                    sleepDurationProvider: (retry, context) => settings.Spectate.RetryTimerSleepDuration,
                    onRetry: (outcome, duration, retryCount, context) =>
                    {
                        logger.LogError($"Timer failed. Waiting {duration} before next retry. Retry attempt {retryCount}");

                        var end = (TimeSpan)context["End"];
                        var state = (State)context["State"];
                        var isMax = retryCount >= settings.Spectate.RetryTimerCountBeforeForceEnd;
                        var isTimerNotFound = outcome.Result == null;
                        var timeToCoreKill = (end - Timer).TotalSeconds;
                        var lastKnownTimeNearCoreKill = timeToCoreKill <= 30;

                        if (state == State.Start)
                        {
                            logger.LogError($"Timer could not be found after {retryCount}. Game still loading.");
                        }
                        else if (state == State.Running && isTimerNotFound && isMax)
                        {
                            logger.LogError($"Timer could not be found after {retryCount}. Shutting down.");
                            context["ForceEnd"] = true;
                        }
                        else if (state == State.Running && isTimerNotFound && !isMax &&  lastKnownTimeNearCoreKill)
                        {
                            logger.LogError($"Timer could not be found after {retryCount} AND is last known to be {timeToCoreKill}s from Core Kill {context["End"]}. Shutting down.");
                            context["ForceEnd"] = true;
                        }
                    })
                .ExecuteAsync((context, token) => controller.TryGetTimerAsync(), context, Token).ConfigureAwait(false);

            return (Timer: timer, ForceEnd: (bool)context["ForceEnd"]);
        }
    }
}