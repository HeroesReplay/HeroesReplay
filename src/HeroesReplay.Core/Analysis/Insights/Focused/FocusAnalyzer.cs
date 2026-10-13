using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Focused;

/// <summary>
/// Counts the seconds the focus timeline (the calculators' pick per second, the one the
/// spectator follows) spent on each player between gates open and the core (#412). It reads the
/// timeline the spectator already built for the replay; it never runs the calculators again.
/// </summary>
public sealed class FocusAnalyzer
{
    public FocusInsights Analyze(
        ReplaySnapshot snapshot,
        IReadOnlyDictionary<TimeSpan, Focus> timeline
    )
    {
        if (snapshot == null || timeline == null || timeline.Count == 0)
        {
            return new FocusInsights();
        }

        var seconds = new Dictionary<int, int>();
        foreach (KeyValuePair<TimeSpan, Focus> second in timeline)
        {
            if (
                second.Value == null
                || second.Key < snapshot.GatesOpen
                || second.Key >= snapshot.End
            )
            {
                continue;
            }

            seconds[second.Value.Index] = seconds.GetValueOrDefault(second.Value.Index) + 1;
        }

        int total = seconds.Values.Sum();
        Dictionary<int, SnapshotPlayer> players = snapshot.Players.ToDictionary(player =>
            player.Index
        );
        List<FocusShare> shares = seconds
            .Where(pair => players.ContainsKey(pair.Key))
            .Select(pair => new FocusShare(
                pair.Key,
                players[pair.Key].Team,
                players[pair.Key].HeroName,
                pair.Value,
                total == 0 ? 0 : Math.Round(pair.Value / (double)total, 3)
            ))
            .OrderByDescending(share => share.Seconds)
            .ThenBy(share => share.PlayerIndex)
            .ToList();
        return new FocusInsights
        {
            Players = shares,
            Top = shares.FirstOrDefault(share => share.Hero != null),
        };
    }
}
