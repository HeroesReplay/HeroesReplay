using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.BSteps;

/// <summary>
/// Finds bstepping: a player starts their Hearthstone and cancels it with a move order at least
/// <see cref="BStepInsightSettings.MinCycles"/> times, each cancel within
/// <see cref="BStepInsightSettings.MaxCancelSeconds"/> of its start and each start within
/// <see cref="BStepInsightSettings.MaxGapSeconds"/> of the last; bursts of it less than
/// <see cref="BStepInsightSettings.MergeSeconds"/> apart are one bstep. Hearthing home and walking off once,
/// or cancelling the channel with an ability, is not bstepping. AI players are left out.
/// <para>
/// Each bstep keeps the hero deaths around it, from <see cref="BStepInsightSettings.DeathBeforeSeconds"/>
/// before it started to <see cref="BStepInsightSettings.DeathAfterSeconds"/> after it ended, that
/// involve the bstepper (they died, or were credited with the kill) or happened within
/// <see cref="BStepInsightSettings.DeathRadius"/> of them: bstepping on a kill, or bstepping and then
/// getting caught.
/// </para>
/// </summary>
public sealed class BStepAnalyzer : IInsightAnalyzer<BStepInsights>
{
    private readonly BStepInsightSettings settings;

    public BStepAnalyzer(BStepInsightSettings settings)
    {
        this.settings = settings ?? new BStepInsightSettings();
    }

    public BStepInsights Analyze(ReplaySnapshot snapshot)
    {
        if (snapshot?.Players == null || snapshot.HearthCasts == null)
        {
            return new BStepInsights();
        }

        var steps = new List<BStep>();
        foreach (SnapshotPlayer player in snapshot.Players.Where(player => !player.IsAi))
        {
            List<HearthCast> cycles = snapshot
                .HearthCasts.Where(cast =>
                    cast.PlayerIndex == player.Index
                    && cast.CancelledAfter is double after
                    && after >= 0
                    && after <= settings.MaxCancelSeconds
                )
                .OrderBy(cast => cast.Seconds)
                .ToList();
            var runs = new List<List<HearthCast>>();
            int first = 0;
            while (first < cycles.Count)
            {
                int last = first;
                while (
                    last + 1 < cycles.Count
                    && cycles[last + 1].Seconds - cycles[last].Seconds <= settings.MaxGapSeconds
                )
                {
                    last++;
                }

                int count = last - first + 1;
                if (count >= Math.Max(2, settings.MinCycles))
                {
                    // A pause to look around and bstep again is the same bstep.
                    if (
                        runs.Count > 0
                        && cycles[first].Seconds - End(runs[^1]) <= settings.MergeSeconds
                    )
                    {
                        runs[^1].AddRange(cycles.GetRange(first, count));
                    }
                    else
                    {
                        runs.Add(cycles.GetRange(first, count));
                    }
                }

                first = last + 1;
            }

            steps.AddRange(runs.Select(run => Step(snapshot, player, run)));
        }

        return new BStepInsights
        {
            BSteps = steps
                .OrderBy(step => step.StartSecond)
                .ThenBy(step => step.PlayerIndex)
                .ToArray(),
        };
    }

    private static double End(List<HearthCast> cycles) =>
        cycles[^1].CancelSeconds ?? cycles[^1].Seconds;

    private BStep Step(ReplaySnapshot snapshot, SnapshotPlayer player, List<HearthCast> cycles)
    {
        double start = cycles[0].Seconds;
        double end = End(cycles);
        List<double> gaps = cycles
            .Skip(1)
            .Select((cast, index) => cast.Seconds - cycles[index].Seconds)
            .Where(gap => gap <= settings.MaxGapSeconds)
            .OrderBy(gap => gap)
            .ToList();
        IReadOnlyList<BStepDeath> deaths = Deaths(snapshot, player, start, end);
        BStepDeath rival = deaths
            .Where(death => !death.After && death.Kind == BStepDeathKind.KilledByBstepper)
            .OrderBy(death => death.Second)
            .FirstOrDefault();
        List<HeroDeath> earlier =
            rival == null
                ? new List<HeroDeath>()
                : (snapshot.HeroDeaths ?? Array.Empty<HeroDeath>())
                    .Where(death => death.Time.TotalSeconds < rival.Second)
                    .ToList();
        return new BStep
        {
            PlayerIndex = player.Index,
            Team = player.Team,
            Hero = player.HeroName,
            StartSecond = (int)Math.Floor(start),
            EndSecond = (int)Math.Floor(end),
            Cycles = cycles.Count,
            MedianGapSeconds = gaps.Count == 0 ? 0 : Math.Round(gaps[gaps.Count / 2], 2),
            Deaths = deaths,
            RivalIndex = rival?.PlayerIndex ?? -1,
            Rival = rival?.Hero,
            // How the bstep came about: dominating that hero, or revenge on them.
            EarlierKillsOnRival = earlier.Count(death =>
                death.PlayerIndex == rival.PlayerIndex && death.KillerIndex == player.Index
            ),
            EarlierDeathsToRival = earlier.Count(death =>
                death.PlayerIndex == player.Index && death.KillerIndex == rival.PlayerIndex
            ),
        };
    }

    private IReadOnlyList<BStepDeath> Deaths(
        ReplaySnapshot snapshot,
        SnapshotPlayer player,
        double start,
        double end
    )
    {
        var linked = new List<BStepDeath>();
        foreach (HeroDeath death in snapshot.HeroDeaths ?? Array.Empty<HeroDeath>())
        {
            double second = death.Time.TotalSeconds;
            if (
                second < start - settings.DeathBeforeSeconds
                || second > end + settings.DeathAfterSeconds
            )
            {
                continue;
            }

            BStepDeathKind? kind = Kind(snapshot, player, death, start, end);
            if (kind == null)
            {
                continue;
            }

            linked.Add(
                new BStepDeath(
                    death.PlayerIndex,
                    snapshot
                        .Players.FirstOrDefault(entry => entry.Index == death.PlayerIndex)
                        ?.HeroName,
                    (int)Math.Floor(second),
                    kind.Value,
                    second > end,
                    death.KillerIndex
                )
            );
        }

        return linked;
    }

    private BStepDeathKind? Kind(
        ReplaySnapshot snapshot,
        SnapshotPlayer player,
        HeroDeath death,
        double start,
        double end
    )
    {
        if (death.PlayerIndex == player.Index)
        {
            // Only a death during or after the bstep: "bsteps, then dies". A hero who died before
            // it and bsteps anyway is a Lost Viking with another Viking down, or Murky back from
            // his egg, and the clip would have nothing to show.
            return death.Time.TotalSeconds >= start ? BStepDeathKind.Bstepper : null;
        }

        if (death.KillerIndex == player.Index)
        {
            return BStepDeathKind.KilledByBstepper;
        }

        if (!Near(snapshot.Positions, player.Index, death, start, end))
        {
            return null;
        }

        return death.Team == player.Team ? BStepDeathKind.Ally : BStepDeathKind.Enemy;
    }

    /// <summary>
    /// Whether the death happened next to the bstepper: where they stood at the death, or, for a
    /// death before the bstep or after the bstepper's own position runs out, at the nearest edge
    /// of the bstep.
    /// </summary>
    private bool Near(
        IHeroPositions positions,
        int player,
        HeroDeath death,
        double start,
        double end
    )
    {
        if (positions == null)
        {
            return false;
        }

        double second = death.Time.TotalSeconds;
        double at = Math.Clamp(second, start, Math.Max(start, end));
        foreach (double moment in new[] { second, at })
        {
            if (
                positions.TryGet(player, TimeSpan.FromSeconds(moment), out MapPoint point)
                && point.DistanceTo(death.Point) <= settings.DeathRadius
            )
            {
                return true;
            }
        }

        return false;
    }
}
