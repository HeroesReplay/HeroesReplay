using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public sealed class StormReplaySpectator : IDisposable
    {
        public event EventHandler<GameEventArgs<StormPlayerDelta>>? HeroChange;
        public event EventHandler<GameEventArgs<GamePanel>>? PanelChange;
        public event EventHandler<GameEventArgs<GameStateDelta>>? StateChange;

        public GamePanel GamePanel
        {
            get => gamePanel;
            private set
            {
                if (gamePanel != value)
                {
                    logger.LogDebug($"[PanelChange][{gamePanel}]");
                    PanelChange?.Invoke(this, new GameEventArgs<GamePanel>(StormReplay, value, GameTimer.Duration(), value.ToString()));
                }

                gamePanel = value;
            }
        }

        public StormPlayer? CurrentPlayer
        {
            get => currentPlayer;
            private set
            {
                if (value != null && value.Player != currentPlayer?.Player)
                {
                    logger.LogDebug($"[HeroChange][{value.Player.HeroId}]");
                    HeroChange?.Invoke(this, new GameEventArgs<StormPlayerDelta>(StormReplay, new StormPlayerDelta(currentPlayer, value), GameTimer.Duration(), value.Event.ToString()));
                }

                currentPlayer = value;
            }
        }

        public GameState GameState
        {
            get => gameState;
            private set
            {
                if (gameState != value)
                {
                    logger.LogDebug($"[StateChange][{gameState}]");
                    StateChange?.Invoke(this, new GameEventArgs<GameStateDelta>(StormReplay, new GameStateDelta(gameState, value), GameTimer.Duration(), gameState.ToString()));
                }
                gameState = value;
            }
        }

        public TimeSpan GameTimer { get; private set; }

        public StormReplay StormReplay
        {
            get => stormReplay;
            private set
            {
                GameState = GameState.StartOfGame;
                GameTimer = TimeSpan.Zero;
                GamePanel = GamePanel.CrowdControlEnemyHeroes;
                CurrentPlayer = null;
                stormReplay = value;
            }
        }

        private CancellationToken Token => tokenProvider.Token;

        private GameState gameState = GameState.StartOfGame;
        private GamePanel gamePanel = GamePanel.CrowdControlEnemyHeroes;

        private StormPlayer? currentPlayer;
        private StormReplay? stormReplay;

        private readonly ILogger<StormReplaySpectator> logger;
        private readonly SpectateTool spectateTool;
        private readonly CancellationTokenProvider tokenProvider;

        public StormReplaySpectator(ILogger<StormReplaySpectator> logger, SpectateTool spectateTool, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.spectateTool = spectateTool;
            this.tokenProvider = tokenProvider;
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            await Task.WhenAll(Task.Run(PanelLoopAsync, Token), Task.Run(FocusLoopAsync, Token), Task.Run(StateLoopAsync, Token));
        }

        private async Task FocusLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                if (!GameState.IsRunning() || GameState.IsEnd() || GameTimer.IsNearStart()) continue;

                try
                {
                    CurrentPlayer = spectateTool.GetStormPlayer(CurrentPlayer, StormReplay, GameTimer);

                    await Task.Delay(CurrentPlayer?.Duration ?? TimeSpan.Zero, Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task PanelLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    GamePanel = spectateTool.GetPanel(StormReplay, GamePanel, GameTimer);

                    await Task.Delay(TimeSpan.FromSeconds(10), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
        }

        private async Task StateLoopAsync()
        {
            while (!GameState.IsEnd())
            {
                Token.ThrowIfCancellationRequested();

                try
                {
                    (TimeSpan next, GameState state) = await spectateTool.GetStateAsync(StormReplay, GameTimer, GameState);

                    GameTimer = next;
                    GameState = state;

                    if (CurrentPlayer != null && GameTimer > TimeSpan.Zero)
                    {
                        logger.LogInformation($"[{CurrentPlayer.Player.HeroId}][{CurrentPlayer.Event}][{CurrentPlayer.Duration}][{GameTimer}][{GameTimer.AddNegativeOffset()}]");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), Token); // End of game
        }

        public void Dispose()
        {
            HeroChange = null;
            PanelChange = null;
            StateChange = null;
        }
    }
}