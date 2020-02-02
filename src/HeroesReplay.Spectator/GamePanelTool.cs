using System;
using System.Linq;
using HeroesReplay.Analyzer;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public class GamePanelTool
    {
        private readonly StormReplayAnalyzer analyzer;

        public GamePanelTool(StormReplayAnalyzer analyzer)
        {
            this.analyzer = analyzer;
        }

        public GamePanel GetPanel(StormReplay replay, GamePanel current, TimeSpan timer)
        {
            if (timer < TimeSpan.FromMinutes(1)) return GamePanel.Talents;

            AnalyzerResult result = analyzer.Analyze(replay, timer, timer.Add(TimeSpan.FromSeconds(5)));

            if (result.Talents.Any()) return GamePanel.Talents;
            if (result.TeamObjectives.Any()) return GamePanel.Experience;
            if (result.MapObjectives.Any()) return GamePanel.Experience;
            if (result.Deaths.Any()) return GamePanel.KillsDeathsAssists;
            return result.Alive.Any() ? GetNextGamePanel(current, result) : current;
        }

        private GamePanel GetNextGamePanel(GamePanel current, AnalyzerResult result)
        {
            return current switch
            {
                GamePanel.KillsDeathsAssists => GamePanel.ActionsPerMinute,
                GamePanel.ActionsPerMinute => result.StormReplay.Replay.MapAlternativeName.ToCarriedObjectivesOr(GamePanel.CrowdControlEnemyHeroes),
                GamePanel.CarriedObjectives => GamePanel.CrowdControlEnemyHeroes,
                GamePanel.CrowdControlEnemyHeroes => GamePanel.DeathDamageRole,
                GamePanel.DeathDamageRole => GamePanel.Experience,
                GamePanel.Experience => GamePanel.Talents,
                GamePanel.Talents => GamePanel.TimeDeadDeathsSelfSustain,
                GamePanel.TimeDeadDeathsSelfSustain => GamePanel.KillsDeathsAssists,
                _ => GamePanel.DeathDamageRole
            };
        }
    }
}