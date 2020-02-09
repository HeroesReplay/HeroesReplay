using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Spectator
{
    public class SpectateTool
    {
        private readonly GamePanelTool panelTool;
        private readonly StormPlayerTool heroTool;
        private readonly GameStateTool stateTool;
        private readonly DebugTool debugTool;

        public SpectateTool(GamePanelTool panelTool, StormPlayerTool heroTool, GameStateTool stateTool, DebugTool debugTool)
        {
            this.panelTool = panelTool;
            this.heroTool = heroTool;
            this.stateTool = stateTool;
            this.debugTool = debugTool;
        }

        public GamePanel? GetPanel(StormReplay stormReplay, GamePanel? current, TimeSpan timer)
        {
            return panelTool.GetPanel(stormReplay, current, timer);
        }

        public StormPlayer? GetStormPlayer(StormPlayer? currentPlayer, StormReplay stormReplay, TimeSpan timer)
        {
            IEnumerable<StormPlayer> stormPlayers = heroTool.GetPlayers(stormReplay, timer);

            if (stormPlayers.Any(p => p.SpectateEvent.IsAny(SpectateEvent.Enemy, SpectateEvent.Alive)))
            {
                if (currentPlayer == null) return stormPlayers.Shuffle().FirstOrDefault();
                return stormPlayers.Where(stormPlayer => stormPlayer.Player != currentPlayer?.Player && stormPlayer.Player.Team != currentPlayer?.Player.Team).Shuffle().FirstOrDefault();
            }

            return stormPlayers.Take(1).Select(stormPlayer => new StormPlayer(stormPlayer.Player, stormPlayer.Timer, stormPlayer.Duration - timer, stormPlayer.SpectateEvent)).FirstOrDefault();
        }

        public async Task<StormState> GetStateAsync(StormReplay stormReplay, StormState currentState)
        {
            return await stateTool.GetStateAsync(stormReplay, currentState);
        }

        public void Debug(StormReplay stormReplay, TimeSpan timer)
        {
            debugTool.Debug(stormReplay, timer);
        }
    }
}