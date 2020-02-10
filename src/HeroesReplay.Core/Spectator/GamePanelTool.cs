using System;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analyzer;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Spectator
{
    public class GamePanelTool
    {
        private readonly StormReplayAnalyzer analyzer;

        public GamePanelTool(StormReplayAnalyzer analyzer)
        {
            this.analyzer = analyzer;
        }

        public GamePanel? GetPanel(Replay replay, GamePanel? current, TimeSpan timer)
        {
            if (timer < TimeSpan.FromMinutes(1)) return GamePanel.Talents;

            AnalyzerResult result = analyzer.Analyze(replay, timer.Subtract(TimeSpan.FromSeconds(5)), timer);

            if (result.TeamTalents.Any()) return GamePanel.Talents;
            if (result.TeamObjectives.Any()) return GamePanel.Experience;
            if (result.MapObjectives.Any()) return GamePanel.Experience;
            if (result.Deaths.Any()) return GamePanel.KillsDeathsAssists;
            return result.Alive.Any() ? GetNextGamePanel(current, result) : current;
        }

        private GamePanel? GetNextGamePanel(GamePanel? current, AnalyzerResult result)
        {
            return current switch
            {
                GamePanel.KillsDeathsAssists => GamePanel.ActionsPerMinute,
                GamePanel.ActionsPerMinute => result.Replay.SupportsCarriedObjectives() ? GamePanel.CarriedObjectives : GamePanel.CrowdControlEnemyHeroes,
                GamePanel.CarriedObjectives => GamePanel.CrowdControlEnemyHeroes,
                GamePanel.CrowdControlEnemyHeroes => GamePanel.DeathDamageRole,
                GamePanel.DeathDamageRole => GamePanel.Experience,
                GamePanel.Experience => GamePanel.Talents,
                GamePanel.Talents => GamePanel.TimeDeadDeathsSelfSustain,
                GamePanel.TimeDeadDeathsSelfSustain => GamePanel.KillsDeathsAssists,
                _ => current
            };
        }
    }
}