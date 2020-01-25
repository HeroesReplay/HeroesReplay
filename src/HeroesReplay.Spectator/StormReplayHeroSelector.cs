using HeroesReplay.Analyzer;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Heroes.ReplayParser;

    public class StormReplayHeroSelector
    {
        private readonly ILogger<StormReplayHeroSelector> logger;

        public StormReplayHeroSelector(ILogger<StormReplayHeroSelector> logger)
        {
            this.logger = logger;
        }

        public IEnumerable<StormPlayer> Select(AnalyzerResult result, GameCriteria gameCriteria) => (gameCriteria switch
        {
            GameCriteria.Alive => HandleAlive(result),
            GameCriteria.PreviousAliveKiller => HandlePreviousKillers(result),
            GameCriteria.Ping => HandlePings(result),
            GameCriteria.Structure => HandleStructures(result),
            GameCriteria.MapObjective => HandleMapObjectives(result),
            GameCriteria.TeamObjective => HandleTeamObjectives(result),
            GameCriteria.CampObjective => HandleCampObjectives(result),
            GameCriteria.Death => HandleDeaths(result),
            GameCriteria.Kill => HandleKills(result, 1),
            GameCriteria.MultiKill => HandleKills(result, 2),
            GameCriteria.TripleKill => HandleKills(result, 3),
            GameCriteria.QuadKill => HandleKills(result, 4),
            GameCriteria.PentaKill => HandleKills(result, 5)
        })
            .OrderBy(x => x.When);

        private IEnumerable<StormPlayer> HandlePreviousKillers(AnalyzerResult result) => result.PreviousKillers.Select(killer => new StormPlayer(killer, result.Start, result.Duration, GameCriteria.PreviousAliveKiller));

        private IEnumerable<StormPlayer> HandleDeaths(AnalyzerResult result) => result.PlayerDeaths.Select(death => new StormPlayer(death.PlayerControlledBy, result.Start, death.TimeSpanDied.Value, GameCriteria.Death));

        private IEnumerable<StormPlayer> HandlePings(AnalyzerResult result) => result.PingSources.Select(ping => new StormPlayer(ping.player, result.Start, ping.TimeSpan, GameCriteria.Ping));

        private IEnumerable<StormPlayer> HandleMapObjectives(AnalyzerResult result) => result.MapObjectives.Select(unit => new StormPlayer(unit.PlayerKilledBy ?? unit.PlayerControlledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.MapObjective));

        private IEnumerable<StormPlayer> HandleStructures(AnalyzerResult result) => result.Structures.Select(unit => new StormPlayer(unit.PlayerKilledBy, result.Start, unit.TimeSpanDied.Value, GameCriteria.Structure));

        private IEnumerable<StormPlayer> HandleAlive(AnalyzerResult result) => result.PlayersAlive.Select(player => new StormPlayer(player, result.Start, result.Duration, GameCriteria.Alive));

        private IEnumerable<StormPlayer> HandleTeamObjectives(AnalyzerResult result) => result.TeamObjectives.Select(objective => new StormPlayer(objective.Player, result.Start, objective.TimeSpan, GameCriteria.TeamObjective));

        /// <summary>
        /// Standard camps are not captured in TeamObjectives
        /// </summary>
        /// <remarks>
        /// https://github.com/barrett777/Heroes.ReplayParser/blob/2d29bf2f66bfd44c471a4214698e6b517d38ecd3/Heroes.ReplayParser/Statistics.cs#L343
        /// </remarks>
        private IEnumerable<StormPlayer> HandleCampObjectives(AnalyzerResult result)
        {
            foreach ((Player player, TimeSpan capture) in result.Camps)
            {
                yield return new StormPlayer(player, result.Start, capture, GameCriteria.CampObjective);
            }
        }

        private IEnumerable<StormPlayer> HandleKills(AnalyzerResult result, int killCount)
        {
            IEnumerable<IGrouping<Player, Unit>> playerKills = result.PlayerDeaths.GroupBy(unit => unit.PlayerKilledBy).Where(kills => kills.Count() == killCount);

            foreach (IGrouping<Player, Unit> players in playerKills)
            {
                Player killer = players.Key;
                TimeSpan maxTime = players.Max(unit => unit.TimeSpanDied.Value);
                Hero? hero = killer.TryGetHero();

                if (hero != null)
                {
                    if (hero.IsMelee)
                    {
                        if (hero == Constants.Heroes.Abathur)
                        {
                            foreach (var death in players)
                            {
                                yield return new StormPlayer(death.PlayerControlledBy, result.Start, maxTime, killCount.ToCriteria());
                            }
                        }
                        else
                        {
                            yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                        }
                    }
                    else if (hero.IsRanged)
                    {
                        yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                    }
                }
                else
                {
                    yield return new StormPlayer(killer, result.Start, maxTime, killCount.ToCriteria());
                }
            }
        }
    }
}
