using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Objectives;

/// <summary>
/// The player who did most for the map objective (#512). The map's score-screen value
/// (<see cref="ObjectiveInsightSettings.Maps"/>) picks the leader; a standout also needs a large
/// share of every player's total, a clear lead over the next player, and the game's own objective
/// award. Measured on 1,975 Storm League games (see <see cref="ObjectiveInsightSettings"/>).
/// </summary>
public sealed class ObjectiveAnalyzer : IInsightAnalyzer<ObjectiveInsights>
{
    private readonly ObjectiveInsightSettings settings;

    public ObjectiveAnalyzer(ObjectiveInsightSettings settings)
    {
        this.settings = settings ?? new ObjectiveInsightSettings();
    }

    public ObjectiveInsights Analyze(ReplaySnapshot snapshot)
    {
        ObjectiveMap map = settings.For(snapshot?.Map);
        if (map == null || snapshot.Players == null)
        {
            return new ObjectiveInsights();
        }

        List<(SnapshotPlayer Player, int Value)> values = snapshot
            .Players.Select(player => (player, Value(player, map.Value)))
            .OrderByDescending(entry => entry.Item2)
            .ToList();
        long total = values.Sum(entry => (long)entry.Value);
        (SnapshotPlayer top, int value) = values.FirstOrDefault();
        if (total <= 0 || top == null || top.IsAi || top.HeroName == null)
        {
            return new ObjectiveInsights { Label = map.Label };
        }

        int next = values.Count > 1 ? values[1].Value : 0;
        var leader = new ObjectiveLeader
        {
            PlayerIndex = top.Index,
            Team = top.Team,
            Hero = top.HeroName,
            Value = value,
            Share = value / (double)total,
            Next = next,
            Award = !string.IsNullOrWhiteSpace(map.Award) && top.Score.HasAward(map.Award),
        };
        return new ObjectiveInsights
        {
            Label = map.Label,
            Leader = leader,
            Standout =
                leader.Share >= settings.MinShare
                && value >= settings.MinLead * next
                && (leader.Award || !settings.RequireAward),
        };
    }

    private static int Value(SnapshotPlayer player, string key) =>
        key != null
        && player.Score.MapValues != null
        && player.Score.MapValues.TryGetValue(key, out int value)
            ? Math.Max(0, value)
            : 0;
}
