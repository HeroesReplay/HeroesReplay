using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Players;

/// <summary>
/// Score lines, the MVP and the positive standouts (#412). AI players never stand out. Measured on
/// 101 games: a deathless carry (0 deaths, 8+ kills) in 11 per 100 games, a kill leader (10+ kills,
/// at most 1 death) in 10 per 100.
/// </summary>
public sealed class PlayerAnalyzer : IInsightAnalyzer<PlayerInsights>
{
    private readonly StandoutSettings settings;

    public PlayerAnalyzer(StandoutSettings settings)
    {
        this.settings = settings ?? new StandoutSettings();
    }

    public PlayerInsights Analyze(ReplaySnapshot snapshot)
    {
        List<PlayerLine> lines = (snapshot?.Players ?? new List<SnapshotPlayer>())
            .Select(player => Line(snapshot, player))
            .ToList();
        var standouts = new List<Standout>();
        foreach (PlayerLine line in lines.Where(line => !line.IsAi && line.Hero != null))
        {
            List<PlayerLine> teammates = lines
                .Where(other => other.Team == line.Team && other.PlayerIndex != line.PlayerIndex)
                .ToList();
            if (line.Deaths == 0 && line.Kills >= settings.DeathlessMinKills)
            {
                standouts.Add(new Standout(StandoutKind.DeathlessCarry, line));
            }
            else if (
                line.Kills >= settings.KillLeaderMinKills
                && line.Deaths <= settings.KillLeaderMaxDeaths
            )
            {
                standouts.Add(new Standout(StandoutKind.KillLeader, line));
            }
            else if (
                line.Kills >= settings.TeamKillMinKills
                && teammates.Count > 0
                && teammates.All(other => line.Kills >= settings.TeamKillFactor * other.Kills)
            )
            {
                standouts.Add(new Standout(StandoutKind.TeamKillLeader, line));
            }
        }

        return new PlayerInsights
        {
            Lines = lines,
            Mvp = lines.FirstOrDefault(line => line.Mvp),
            Standouts = standouts
                .OrderBy(standout => standout.Kind)
                .ThenByDescending(standout => standout.Player.Kills)
                .ThenBy(standout => standout.Player.Deaths)
                .ToList(),
        };
    }

    private static PlayerLine Line(ReplaySnapshot snapshot, SnapshotPlayer player)
    {
        int teamDamage = snapshot
            .Players.Where(other => other.Team == player.Team)
            .Sum(other => other.Score.HeroDamage);
        return new PlayerLine
        {
            PlayerIndex = player.Index,
            Team = player.Team,
            Hero = player.HeroName,
            IsAi = player.IsAi,
            Kills = player.Score.Kills,
            Deaths = player.Score.Deaths,
            Assists = player.Score.Assists,
            Takedowns = player.Score.Takedowns,
            HeroDamage = player.Score.HeroDamage,
            HeroDamageShare = teamDamage <= 0 ? 0 : player.Score.HeroDamage / (double)teamDamage,
            Mvp = player.Score.Mvp,
        };
    }
}
