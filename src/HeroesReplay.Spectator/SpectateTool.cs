using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public class SpectateTool
    {
        private readonly GamePanelTool panelTool;
        private readonly StormPlayerTool heroTool;
        private readonly GameStateTool stateTool;

        public SpectateTool(GamePanelTool panelTool, StormPlayerTool heroTool, GameStateTool stateTool)
        {
            this.panelTool = panelTool;
            this.heroTool = heroTool;
            this.stateTool = stateTool;
        }

        public GamePanel GetPanel(StormReplay stormReplay, GamePanel current, TimeSpan timer)
        {
            return this.panelTool.GetPanel(stormReplay, current, timer);
        }

        public StormPlayer GetStormPlayer(StormPlayer? currentPlayer, StormReplay stormReplay, TimeSpan timer)
        {
            List<StormPlayer> stormPlayers = this.heroTool.GetPlayers(stormReplay, timer);

            if (stormPlayers.Any(p => p.Event.IsAny(GameEvent.EnemyProximity, GameEvent.Alive)))
            {
                return stormPlayers.Where(p => p.Player != currentPlayer?.Player && currentPlayer?.Player.Team != p.Player.Team).Shuffle().FirstOrDefault();
            }

            return stormPlayers.Take(1).Select(p => new StormPlayer(p.Player, p.Timer, p.Duration - timer, p.Event)).FirstOrDefault();
        }

        public async Task<(TimeSpan, GameState)> GetStateAsync(StormReplay stormReplay, TimeSpan timer, GameState state)
        {
            return await this.stateTool.GetStateAsync(stormReplay, timer, state);
        }
    }
}