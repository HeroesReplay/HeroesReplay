using System;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Finish;

/// <summary>
/// Finds a core race (#412). Core health is not in the replay, so the race is inferred: both
/// teams lost at least <see cref="FinishInsightSettings.MinKeepsLostEach"/> keeps, and at least
/// <see cref="FinishInsightSettings.MinAttackers"/> heroes of the team whose core fell were within
/// <see cref="FinishInsightSettings.CoreRadius"/> of the other core in the final window.
/// Measured on 101 games: about 1 in 100.
/// </summary>
public sealed class FinishAnalyzer : IInsightAnalyzer<FinishInsights>
{
    private readonly FinishInsightSettings settings;

    public FinishAnalyzer(FinishInsightSettings settings)
    {
        this.settings = settings ?? new FinishInsightSettings();
    }

    public FinishInsights Analyze(ReplaySnapshot snapshot)
    {
        if (snapshot?.CoreLostBy is not int fallen || snapshot.Cores.Count != 2 || snapshot.Aram)
        {
            return new FinishInsights();
        }

        int keepsEach = Enumerable
            .Range(0, 2)
            .Min(team => snapshot.TownLosses.Count(loss => loss.Team == team && loss.Keep));
        MapPoint otherCore = snapshot.Cores[1 - fallen];
        int attackers = snapshot
            .Players.Where(player => player.Team == fallen)
            .Count(player => WasNear(snapshot, player.Index, otherCore));
        return new FinishInsights
        {
            KeepsLostEach = keepsEach,
            AttackersAtOtherCore = attackers,
            CoreRace = keepsEach >= settings.MinKeepsLostEach && attackers >= settings.MinAttackers,
        };
    }

    private bool WasNear(ReplaySnapshot snapshot, int playerIndex, MapPoint core)
    {
        int seconds = (int)Math.Ceiling(settings.FinalWindow.TotalSeconds);
        for (int back = 0; back <= seconds; back++)
        {
            TimeSpan time = snapshot.End - TimeSpan.FromSeconds(back);
            if (
                snapshot.Positions != null
                && snapshot.Positions.TryGet(playerIndex, time, out MapPoint point)
                && point.DistanceTo(core) < settings.CoreRadius
            )
            {
                return true;
            }
        }

        return false;
    }
}
