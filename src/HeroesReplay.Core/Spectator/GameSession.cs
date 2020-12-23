using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameSession : IGameSession
    {
        private readonly IGameController controller;
        private readonly ILogger<GameSession> logger;
        private readonly Settings settings;
        private readonly ISessionHolder sessionHolder;
        private readonly CancellationToken token;

        private State State { get; set; }

        private ZoomLevel ZoomLevel { get; set; }

        private bool ControlsHiddenSet { get; set; }

        private bool FollowModeSet { get; set; }

        private TimeSpan Timer { get; set; }

        private Focus? Focus { get; set; }

        private SessionData Data => sessionHolder.SessionData;

        public GameSession(ILogger<GameSession> logger, Settings settings, ISessionHolder sessionHolder, IGameController controller, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.settings = settings;
            this.sessionHolder = sessionHolder;
            this.controller = controller;
            this.token = tokenProvider.Token;
        }

        public async Task SpectateAsync()
        {
            State = State.Start;
            Timer = default(TimeSpan);
            FollowModeSet = false;
            ControlsHiddenSet = false;
            ZoomLevel = default(ZoomLevel);

            await Task.WhenAll(
                Task.Run(PanelLoopAsync, token),
                Task.Run(FocusLoopAsync, token),
                Task.Run(StateLoopAsync, token),
                Task.Run(ConfigureLoopAsync, token));
        }

        private async Task StateLoopAsync()
        {
            while (State != State.End)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    TimeSpan? timer = await GetOcrTimer();

                    if (timer.HasValue)
                    {
                        // Dont rely on the unique second, incase it blips
                        if (timer.Value.Add(TimeSpan.FromSeconds(5)) >= Data.End) State = State.End;
                        else if (Timer == timer.Value && Timer != TimeSpan.Zero) State = State.Paused;
                        else if (timer.Value > Timer) State = State.Running;
                        else State = State.Start;

                        Timer = timer.Value;

                        logger.LogInformation($"{State}, {Timer}");
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete state loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            await Task.Delay(settings.Spectate.EndScreenTime);
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
                            ConfigureControls();

                        await Task.Delay(500);

                        //if (settings.Spectate.ZoomLevel != ZoomLevel) 
                        //    ConfigureZoom();

                        await Task.Delay(250);

                        if (settings.Spectate.ZoomLevel == ZoomLevel && !FollowModeSet)
                            ConfigureFollowMode();

                        await Task.Delay(250);
                    }

                    var configured = new[]
                    {
                        settings.Spectate.ZoomLevel == ZoomLevel,
                        FollowModeSet,
                        ControlsHiddenSet

                    }.All(configured => configured == true);

                    if (configured)
                    {
                        logger.LogInformation("Client configured.");
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
                token.ThrowIfCancellationRequested();

                try
                {
                    if (State == State.Running && Data.Players.TryGetValue(Timer, out Focus focus) && focus.Index != index)
                    {
                        /* We keep a track of the previous index so we dont send too many commands on the same hero, 
                         * because double tapping a hero will then change the spectate behaviour
                         */
                        Focus = focus;
                        index = focus.Index;

                        logger.LogInformation($"Selecting {focus.Target.HeroId}. Description: {focus.Description}");
                        controller.SendFocus(focus.Index);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete focus loop");
                }

                await Task.Delay(950);
            }
        }

        private async Task PanelLoopAsync()
        {
            Panel previous = Panel.None;
            Panel next = Panel.None;

            while (State != State.End)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    if (Data.Panels.TryGetValue(Timer, out var panel) && panel != previous)
                    {
                        var name = Enum.GetName(typeof(Panel), panel);
                        logger.LogInformation($"Selecting panel: {name}");
                        controller.SendPanel(panel);
                    }
                    else
                    {
                        next = previous switch
                        {
                            Panel.None => Panel.Talents,
                            Panel.KillsDeathsAssists => Panel.ActionsPerMinute,
                            Panel.ActionsPerMinute => Data.IsCarriedObjectiveMap ? Panel.CarriedObjectives : Panel.CrowdControlEnemyHeroes,
                            Panel.CarriedObjectives => Panel.CrowdControlEnemyHeroes,
                            Panel.CrowdControlEnemyHeroes => Panel.DeathDamageRole,
                            Panel.DeathDamageRole => Panel.Experience,
                            Panel.Experience => Panel.Talents,
                            Panel.Talents => Panel.TimeDeadDeathsSelfSustain,
                            Panel.TimeDeadDeathsSelfSustain => Panel.KillsDeathsAssists,
                            _ => Panel.Talents
                        };
                    }

                    if (next != previous)
                    {
                        previous = next;
                        controller.SendPanel(next);
                    }

                    await Task.Delay(settings.Spectate.PanelRotateTime, token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not complete panel loop");
                }
            }
        }

        private void ConfigureFollowMode()
        {
            if (Focus != null)
            {
                logger.LogInformation("A player has been selected, can now set follow selected unit mode.");
                controller.CameraFollow();
                FollowModeSet = true;
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

        private async Task<TimeSpan?> GetOcrTimer()
        {
            return await Policy
                 .HandleResult<TimeSpan?>(timer =>
                 {
                     if (timer == null) return false;

                     if (Timer != TimeSpan.Zero && (timer > Timer.Add(TimeSpan.FromSeconds(10)) || timer < Timer.Subtract(TimeSpan.FromSeconds(10))))
                     {
                         logger.LogInformation($"OCR Timer is not an expected value? Before: {Timer}, After: {timer}");
                         return false;
                     }

                     return true;
                 })
                 .WaitAndRetryAsync(retryCount: 5, retry => TimeSpan.FromSeconds(1))
                 .ExecuteAsync(async (t) => await controller.TryGetTimerAsync(), token);
        }

    }
}