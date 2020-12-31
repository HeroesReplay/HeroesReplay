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
        private readonly Settings settings;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ISessionHolder sessionHolder;

        private CancellationToken Token => tokenProvider.Token;

        private State State { get; set; }

        private bool ControlsHiddenSet { get; set; }

        private TimeSpan Timer { get; set; }

        private SessionData Data => sessionHolder.SessionData;

        public GameSession(ILogger<GameSession> logger, Settings settings, ISessionHolder sessionHolder, IGameController controller, CancellationTokenProvider tokenProvider)
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
            Timer = default(TimeSpan);
            ControlsHiddenSet = false;

            await Task.WhenAll(
                Task.Run(PanelLoopAsync, Token),
                Task.Run(FocusLoopAsync, Token),
                Task.Run(StateLoopAsync, Token),
                Task.Run(ConfigureLoopAsync, Token));

            await Task.Delay(settings.Spectate.EndScreenTime);
        }

        private async Task StateLoopAsync()
        {
            while (State != State.End)
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    TimeSpan? timer = await GetOcrTimer();

                    if (timer.HasValue)
                    {
                        if (timer.Value.Add(settings.Spectate.EndCoreTime) >= Data.End) State = State.End;
                        else if (Timer == timer.Value && Timer != TimeSpan.Zero) State = State.Paused;
                        else if (timer.Value > Timer) State = State.Running;
                        else State = State.Start;

                        Timer = timer.Value;

                        logger.LogDebug($"{State}, {Timer}");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete state loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), Token);
            }
        }

        private async Task ConfigureLoopAsync()
        {
            while (State != State.End)
            {
                try
                {
                    if (State == State.Running)
                    {
                        if (!ControlsHiddenSet)
                        {
                            ConfigureControls();
                        }
                    }

                    if (ControlsHiddenSet)
                    {
                        logger.LogDebug("Client configured.");
                        return;
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete configure loop");
                }

                await Task.Delay(250);
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
                    if (State == State.Running && Data.Players.TryGetValue(Timer, out Focus? focus) && focus.Index != index)
                    {
                        /* We keep a track of the previous index so we dont send too many commands on the same hero, 
                         * because double tapping a hero will then change the spectate behaviour
                         */
                        index = focus.Index;

                        logger.LogInformation($"Selecting {focus.Target.HeroId}. Description: {focus.Description}");
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
            TimeSpan delay = TimeSpan.FromSeconds(1);

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
                            };
                        }

                        if (next != previous)
                        {
                            controller.SendPanel(next);
                            cooldown = settings.Spectate.PanelRotateTime;
                            previous = next;
                        }

                        cooldown = cooldown.Subtract(delay);
                        await Task.Delay(delay);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Could not complete panel loop");
                    }
                }
            }
        }

        private void ConfigureControls()
        {
            if (!ControlsHiddenSet)
            {
                controller.ToggleControls();
                ControlsHiddenSet = true;
            }
        }

        private static readonly TimeSpan invalidThreshold = TimeSpan.FromSeconds(15);

        private async Task<TimeSpan?> GetOcrTimer()
        {
            return await Policy
                 .HandleResult<TimeSpan?>(timer =>
                 {
                     if (timer == null) return false;

                     if (Timer != TimeSpan.Zero && (timer > Timer.Add(invalidThreshold) || timer < Timer.Subtract(invalidThreshold)))
                     {
                         logger.LogDebug($"OCR Timer is not an expected value? Before: {Timer}, After: {timer}");
                         return false;
                     }

                     return true;
                 })
                 .WaitAndRetryAsync(retryCount: 5, retry => TimeSpan.FromSeconds(1))
                 .ExecuteAsync(async (t) => await controller.TryGetTimerAsync(), Token);
        }

    }
}