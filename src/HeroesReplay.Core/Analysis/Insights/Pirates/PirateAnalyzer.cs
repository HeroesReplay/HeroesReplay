using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Pirates;

/// <summary>
/// Finds a Juice Pirates game (<see cref="PirateInsights"/>). A team qualifies with a carrier (Lt.
/// Morales who took Medivac Dropship, or Medivh) and at least
/// <see cref="PirateInsightSettings.MinDives"/> base dives: from gates open, sampled every
/// <see cref="PirateInsightSettings.SampleEvery"/>, at least <see cref="PirateInsightSettings.MinDivers"/>
/// of its heroes within <see cref="PirateInsightSettings.Radius"/> of an enemy fort, keep (still
/// standing) or core, with at most <see cref="PirateInsightSettings.MaxDefenders"/> enemy heroes
/// there, for <see cref="PirateInsightSettings.MinDiveSamples"/> samples in a row. Dives closer than
/// <see cref="PirateInsightSettings.DiveGap"/> are one dive. A normal late push brings defenders,
/// so the empty-base rule is what tells a backdoor apart.
/// </summary>
public sealed class PirateAnalyzer : IInsightAnalyzer<PirateInsights>
{
    private readonly PirateInsightSettings settings;

    public PirateAnalyzer(PirateInsightSettings settings)
    {
        this.settings = settings ?? new PirateInsightSettings();
    }

    public PirateInsights Analyze(ReplaySnapshot snapshot)
    {
        if (
            snapshot?.Players == null
            || snapshot.Aram
            || snapshot.Positions == null
            || snapshot.Cores == null
            || snapshot.Cores.Count != 2
        )
        {
            return new PirateInsights();
        }

        PirateInsights best = null;
        for (int team = 0; team < 2; team++)
        {
            List<SnapshotPlayer> crew = snapshot
                .Players.Where(player => player.Team == team)
                .ToList();
            string carrier = Carrier(crew);
            if (carrier == null)
            {
                continue;
            }

            int dives = Dives(snapshot, team);
            if (dives < settings.MinDives || (best != null && best.Dives >= dives))
            {
                continue;
            }

            best = new PirateInsights
            {
                Pirates = true,
                Team = team,
                Carrier = carrier,
                Sanctification = crew.Any(player =>
                    Is(player, "Tyrael") && HasTalent(player, settings.SanctificationTalent)
                ),
                Heroes = crew.Select(player => player.Hero?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray(),
                Dives = dives,
                Sunk = snapshot.CoreLostBy == 1 - team,
            };
        }

        return best ?? new PirateInsights();
    }

    private string Carrier(IReadOnlyList<SnapshotPlayer> crew)
    {
        SnapshotPlayer morales = crew.FirstOrDefault(player =>
            Is(player, "Lt. Morales") && HasTalent(player, settings.MedivacTalent)
        );
        if (morales != null)
        {
            return morales.Hero.Name;
        }

        SnapshotPlayer medivh = settings.Medivh
            ? crew.FirstOrDefault(player => Is(player, "Medivh"))
            : null;
        return medivh?.Hero.Name;
    }

    private int Dives(ReplaySnapshot snapshot, int team)
    {
        int enemy = 1 - team;
        int[] attackers = snapshot
            .Players.Where(p => p.Team == team)
            .Select(p => p.Index)
            .ToArray();
        int[] defenders = snapshot
            .Players.Where(p => p.Team == enemy)
            .Select(p => p.Index)
            .ToArray();
        List<Town> towns = (snapshot.Towns ?? Array.Empty<Town>())
            .Where(town => town.Team == enemy)
            .ToList();
        MapPoint core = snapshot.Cores[enemy];

        int dives = 0;
        int streak = 0;
        TimeSpan? lastDive = null;
        TimeSpan step =
            settings.SampleEvery <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : settings.SampleEvery;
        for (TimeSpan time = snapshot.GatesOpen; time <= snapshot.End; time += step)
        {
            bool diving = Targets(snapshot, towns, core, time)
                .Any(target =>
                    Near(snapshot, attackers, target, time) >= settings.MinDivers
                    && Near(snapshot, defenders, target, time) <= settings.MaxDefenders
                );
            streak = diving ? streak + 1 : 0;
            if (streak != settings.MinDiveSamples)
            {
                continue;
            }

            if (lastDive == null || time - lastDive.Value >= settings.DiveGap)
            {
                dives++;
            }

            lastDive = time;
        }

        return dives;
    }

    private static IEnumerable<MapPoint> Targets(
        ReplaySnapshot snapshot,
        IReadOnlyList<Town> towns,
        MapPoint core,
        TimeSpan time
    )
    {
        foreach (Town town in towns)
        {
            bool fallen = (snapshot.TownLosses ?? Array.Empty<TownLoss>()).Any(loss =>
                loss.TownId == town.Id && loss.Time <= time
            );
            if (!fallen)
            {
                yield return town.Point;
            }
        }

        yield return core;
    }

    private int Near(
        ReplaySnapshot snapshot,
        IEnumerable<int> players,
        MapPoint target,
        TimeSpan time
    ) =>
        players.Count(index =>
            snapshot.Positions.TryGet(index, time, out MapPoint point)
            && point.DistanceTo(target) <= settings.Radius
        );

    private static bool Is(SnapshotPlayer player, string hero) =>
        string.Equals(player?.Hero?.Name, hero, StringComparison.OrdinalIgnoreCase);

    private static bool HasTalent(SnapshotPlayer player, string talent) =>
        !string.IsNullOrWhiteSpace(talent)
        && (player.Talents ?? Array.Empty<string>()).Any(name =>
            name != null && name.Contains(talent, StringComparison.OrdinalIgnoreCase)
        );
}
