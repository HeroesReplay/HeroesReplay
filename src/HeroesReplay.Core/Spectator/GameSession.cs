using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameSession : IGameSession
    {
        private readonly IGameController controller;
        private readonly ILogger<GameSession> logger;
        private readonly ISessionHolder sessionReader;
        private readonly CancellationToken token;

        private State State { get; set; } = State.Start;
        private TimeSpan Timer { get; set; }

        private SessionData Data => sessionReader.SessionData;

        public GameSession(ILogger<GameSession> logger, ISessionHolder sessionReader, IGameController controller, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.sessionReader = sessionReader;
            this.controller = controller;
            this.token = tokenProvider.Token;
        }

        public async Task SpectateAsync()
        {
            State = State.Start;
            Timer = default(TimeSpan);

            await Task.WhenAll(Task.Run(PanelLoopAsync, token), Task.Run(FocusLoopAsync, token), Task.Run(StateLoopAsync, token));
        }

        private async Task StateLoopAsync()
        {
            while (State != State.End)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var timer = await controller.TryGetTimerAsync();

                    if (timer != null)
                    {
                        // sometimes, OCR gets it wrong, shall we skip the current suggestion
                        if (Timer != TimeSpan.Zero && (timer > Timer.Add(TimeSpan.FromSeconds(30)) || timer < Timer.Subtract(TimeSpan.FromSeconds(30))))
                        {
                            logger.LogInformation($"OCR Timer is a bit fuzzy? Before: {Timer}, After: {timer}");
                        }
                        else
                        {
                            if (timer.Value >= Data.End) State = State.End;
                            else if (Timer == timer.Value) State = State.Paused;
                            else if (timer.Value > Timer) State = State.Running;
                            else State = State.Start;
                            Timer = timer.Value;
                        }
                    }
                }
                catch
                {

                }

                logger.LogInformation($"{State}, {Timer}");

                await Task.Delay(TimeSpan.FromSeconds(1), token);
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
                    if (State == State.Running && Data.Players.TryGetValue(Timer, out var output) && output.Index != index)
                    {
                        logger.LogInformation($"Selecting {output.Player.Character}. Description: {output.Description}");
                        controller.SendFocus(output.Index);
                        index = output.Index;
                    }
                }
                catch (Exception e)
                {

                }

                await Task.Delay(1000);
            }
        }

        private async Task PanelLoopAsync()
        {
            Panel panel = Panel.Talents;

            while (State != State.End)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    Panel next;

                    if (Timer < TimeSpan.FromMinutes(1)) next = Panel.Talents;
                    else if (Data.Panels.TryGetValue(Timer, out next))
                    {

                    }
                    else
                    {
                        next = panel switch
                        {
                            Panel.KillsDeathsAssists => Panel.ActionsPerMinute,
                            Panel.ActionsPerMinute => Data.IsCarriedObjectiveMap ? Panel.CarriedObjectives : Panel.CrowdControlEnemyHeroes,
                            Panel.CarriedObjectives => Panel.CrowdControlEnemyHeroes,
                            Panel.CrowdControlEnemyHeroes => Panel.DeathDamageRole,
                            Panel.DeathDamageRole => Panel.Experience,
                            Panel.Experience => Panel.Talents,
                            Panel.Talents => Panel.TimeDeadDeathsSelfSustain,
                            Panel.TimeDeadDeathsSelfSustain => Panel.KillsDeathsAssists
                        };
                    }

                    if (next != panel)
                    {
                        panel = next;

                        var name = Enum.GetName(typeof(Panel), next);
                        logger.LogInformation($"Selecting panel: {name}");

                        controller.SendPanel((int)panel);
                    }
                }
                catch
                {

                }

                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }

        }
    }
}