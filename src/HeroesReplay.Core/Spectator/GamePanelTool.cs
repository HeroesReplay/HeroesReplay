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
        private readonly ReplayHelper replayHelper;

        public GamePanelTool(StormReplayAnalyzer analyzer, ReplayHelper replayHelper)
        {
            this.analyzer = analyzer;
            this.replayHelper = replayHelper;
        }

        public GamePanel? GetPanel(Replay replay, GamePanel? current, TimeSpan timer)
        {
            if (timer < TimeSpan.FromMinutes(1)) return GamePanel.Talents;

            AnalyzerResult result = analyzer.Analyze(replay, timer.Subtract(TimeSpan.FromSeconds(5)), timer);

            if (result.TeamTalents.Any() && current != GamePanel.Talents) return GamePanel.Talents;
            return GetNextGamePanel(current, result);
        }

        private GamePanel? GetNextGamePanel(GamePanel? current, AnalyzerResult result)
        {
            return current switch
            {
                GamePanel.KillsDeathsAssists => GamePanel.ActionsPerMinute,
                GamePanel.ActionsPerMinute => replayHelper.IsCarriedObjectiveMap(result.Replay) ? GamePanel.CarriedObjectives : GamePanel.CrowdControlEnemyHeroes,
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