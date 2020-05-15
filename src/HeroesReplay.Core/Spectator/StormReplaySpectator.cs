using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static HeroesReplay.Core.Shared.Constants;

namespace HeroesReplay.Core.Spectator
{
    public sealed class StormReplaySpectator
    {
        public event EventHandler<GameEventArgs<Delta<StormPlayer>>> HeroChange;
        public event EventHandler<GameEventArgs<Delta<GamePanel?>>> PanelChange;
        public event EventHandler<GameEventArgs<Delta<StormState>>> StateChange;
        public event EventHandler<EventArgs> GatesOpened;

        public GamePanel? CurrentPanel
        {
            get => currentPanel;
            private set
            {
                if (currentPanel != value)
                {
                    PanelChange?.Invoke(this, new GameEventArgs<Delta<GamePanel?>>(StormReplay, new Delta<GamePanel?>(CurrentPanel, value), CurrentState.Timer, value?.ToString() ?? "None"));
                }

                currentPanel = value;
            }
        }

        public StormPlayer? CurrentPlayer
        {
            get => currentPlayer;
            private set
            {
                if (value != null && value != currentPlayer && value?.Player != currentPlayer?.Player)
                {
                    HeroChange?.Invoke(this, new GameEventArgs<Delta<StormPlayer>>(StormReplay, new Delta<StormPlayer>(CurrentPlayer, value), CurrentState.Timer, value.SpectateEvent.ToString()));
                }

                currentPlayer = value;
            }
        }

        public StormState CurrentState
        {
            get => currentState;
            private set
            {
                if (value.State != currentState.State)
                {
                    StateChange?.Invoke(this, new GameEventArgs<Delta<StormState>>(StormReplay, new Delta<StormState>(currentState, value), value.Timer, value.State.ToString()));
                }
                currentState = value;
            }
        }

        public StormReplay? StormReplay
        {
            get => stormReplay;
            private set
            {
                stormReplay = value;
                currentPlayer = null;
                currentState = StormState.Start;
                currentPanel = GamePanel.CrowdControlEnemyHeroes;
            }
        }

        private CancellationToken Token => tokenProvider.Token;

        private GamePanel? currentPanel;
        private StormPlayer? currentPlayer;
        private StormReplay? stormReplay;
        private StormState currentState = StormState.Start;

        private readonly ILogger<StormReplaySpectator> logger;
        private readonly SpectateTool spectateTool;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly Settings settings;

        public StormReplaySpectator(ILogger<StormReplaySpectator> logger, SpectateTool spectateTool, CancellationTokenProvider tokenProvider, IOptions<Settings> settings)
        {
            this.logger = logger;
            this.spectateTool = spectateTool;
            this.tokenProvider = tokenProvider;
            this.settings = settings.Value;
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            await Task.WhenAll(Task.Run(PanelLoopAsync, Token), Task.Run(FocusLoopAsync, Token), Task.Run(StateLoopAsync, Token));
            await Task.Delay(settings.EndScreenTime);
        }

        private async Task FocusLoopAsync()
        {
            while (!CurrentState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    if (CurrentState.IsRunning())
                    {
                        CurrentPlayer = spectateTool.GetStormPlayer(CurrentPlayer, StormReplay, CurrentState.Timer);
                        await CurrentPlayer.SpectateAsync(Token).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task PanelLoopAsync()
        {
            while (!CurrentState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    CurrentPanel = spectateTool.GetPanel(StormReplay, CurrentPanel, CurrentState.Timer);

                    await Task.Delay(TimeSpan.FromSeconds(CurrentPanel switch { GamePanel.Talents => 20, _ => 10 }), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (!CurrentState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    CurrentState = await spectateTool.GetStateAsync(StormReplay, CurrentState);

                    logger.LogInformation($"{CurrentPlayer}, {CurrentState}, {CurrentPanel}");

                    spectateTool.Debug(stormReplay, currentState.Timer);

                    await Task.Delay(TimeSpan.FromSeconds(1), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }
    }
}