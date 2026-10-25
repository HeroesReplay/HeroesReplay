using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Bursts;

/// <summary>
/// One player's hero killing blows in a burst (#512), by <see cref="KillBursts"/>: at least
/// <c>MinimumKills</c> within <c>SpanSeconds</c> of the first. AI players are left out. On 1,965
/// Storm League games, 3 within 3 s happened in 55 games, and twice by one hero in 2.
/// </summary>
public sealed class BurstAnalyzer : IInsightAnalyzer<BurstInsights>
{
    private readonly BurstInsightSettings settings;

    public BurstAnalyzer(BurstInsightSettings settings)
    {
        this.settings = settings ?? new BurstInsightSettings();
    }

    public BurstInsights Analyze(ReplaySnapshot snapshot)
    {
        if (snapshot?.Players == null || snapshot.KillingBlows == null)
        {
            return new BurstInsights();
        }

        var players = new List<PlayerBursts>();
        foreach (SnapshotPlayer player in snapshot.Players.Where(player => !player.IsAi))
        {
            List<int> seconds = snapshot
                .KillingBlows.Where(blow => blow.KillerIndex == player.Index)
                .Select(blow => blow.Second)
                .OrderBy(second => second)
                .ToList();
            IReadOnlyList<KillBurst> bursts = KillBursts.Find(
                seconds,
                settings.MinimumKills,
                settings.SpanSeconds
            );
            if (bursts.Count == 0 || player.HeroName == null)
            {
                continue;
            }

            players.Add(
                new PlayerBursts
                {
                    PlayerIndex = player.Index,
                    Team = player.Team,
                    Hero = player.HeroName,
                    Bursts = bursts
                        .Select(burst => new Burst(burst.StartSecond, burst.Kills))
                        .ToArray(),
                }
            );
        }

        return new BurstInsights
        {
            Players = players
                .OrderByDescending(player => player.Bursts.Count)
                .ThenBy(player => player.Bursts[0].Second)
                .ToList(),
        };
    }
}
