using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis.Insights.Awards;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Bursts;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Comeback;
using HeroesReplay.Core.Analysis.Insights.Finish;
using HeroesReplay.Core.Analysis.Insights.Focused;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Objectives;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Analysis.Insights.Structures;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>Reads a parsed replay's <see cref="MatchInsights"/>.</summary>
public interface IMatchInsightsReader
{
    /// <summary>
    /// The replay's insights. <paramref name="focus"/> is the spectator's focus timeline for the
    /// replay; without it the most followed hero is left out.
    /// </summary>
    MatchInsights Read(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        IReadOnlyDictionary<TimeSpan, Focus> focus = null
    );
}

/// <summary>
/// Reads the replay's facts once (<see cref="ReplaySnapshotReader"/>) and hands them to each
/// analyzer. The analyzers are independent; this class only composes them.
/// </summary>
public sealed class MatchInsightsReader : IMatchInsightsReader
{
    private readonly InsightsSettings settings;
    private readonly IInsightAnalyzer<PlayerInsights> players;
    private readonly IInsightAnalyzer<LaneInsights> lanes;
    private readonly IInsightAnalyzer<CampInsights> camps;
    private readonly IInsightAnalyzer<StructureInsights> structures;
    private readonly IInsightAnalyzer<FinishInsights> finish;
    private readonly IInsightAnalyzer<ComebackInsights> comeback;
    private readonly IInsightAnalyzer<BuildInsights> builds;
    private readonly FocusAnalyzer focusAnalyzer;
    private readonly IInsightAnalyzer<BurstInsights> bursts;
    private readonly IInsightAnalyzer<ObjectiveInsights> objectives;
    private readonly IInsightAnalyzer<AwardInsights> awards;
    private readonly IInsightAnalyzer<BossFightInsights> bosses;

    public MatchInsightsReader(
        InsightsSettings settings,
        IInsightAnalyzer<PlayerInsights> players,
        IInsightAnalyzer<LaneInsights> lanes,
        IInsightAnalyzer<CampInsights> camps,
        IInsightAnalyzer<StructureInsights> structures,
        IInsightAnalyzer<FinishInsights> finish,
        IInsightAnalyzer<ComebackInsights> comeback,
        IInsightAnalyzer<BuildInsights> builds,
        FocusAnalyzer focus,
        IInsightAnalyzer<BurstInsights> bursts,
        IInsightAnalyzer<ObjectiveInsights> objectives,
        IInsightAnalyzer<AwardInsights> awards,
        IInsightAnalyzer<BossFightInsights> bosses
    )
    {
        this.bosses = bosses ?? throw new ArgumentNullException(nameof(bosses));
        this.settings = settings ?? new InsightsSettings();
        this.bursts = bursts ?? throw new ArgumentNullException(nameof(bursts));
        this.objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
        this.awards = awards ?? throw new ArgumentNullException(nameof(awards));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        this.camps = camps ?? throw new ArgumentNullException(nameof(camps));
        this.structures = structures ?? throw new ArgumentNullException(nameof(structures));
        this.finish = finish ?? throw new ArgumentNullException(nameof(finish));
        this.comeback = comeback ?? throw new ArgumentNullException(nameof(comeback));
        this.builds = builds ?? throw new ArgumentNullException(nameof(builds));
        focusAnalyzer = focus ?? new FocusAnalyzer();
    }

    /// <summary>The reader with every analyzer, for callers without dependency injection.</summary>
    public static MatchInsightsReader Create(InsightsSettings settings, IGuideCatalog guides)
    {
        settings ??= new InsightsSettings();
        var matcher = new BuildMatcher(
            new BuildMatchOptions
            {
                MinTiers = settings.Builds.MinTiers,
                SituationalWeight = settings.Builds.SituationalWeight,
                AmbiguityMargin = settings.Builds.AmbiguityMargin,
            }
        );
        return new MatchInsightsReader(
            settings,
            new PlayerAnalyzer(settings.Standouts),
            new LaneAnalyzer(settings.Lanes),
            new CampAnalyzer(settings.Camps),
            new StructureAnalyzer(),
            new FinishAnalyzer(settings.Finish),
            new ComebackAnalyzer(settings.Comeback),
            new BuildAnalyzer(guides, matcher),
            new FocusAnalyzer(),
            new BurstAnalyzer(settings.Bursts),
            new ObjectiveAnalyzer(settings.Objectives),
            new AwardAnalyzer(settings.Awards),
            new BossFightAnalyzer(settings.Bosses)
        );
    }

    public MatchInsights Read(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        IReadOnlyDictionary<TimeSpan, Focus> focus = null
    )
    {
        ReplaySnapshot snapshot = ReplaySnapshotReader.Read(replay, catalog, settings);
        return new MatchInsights
        {
            Players = players.Analyze(snapshot),
            Lanes = lanes.Analyze(snapshot),
            Camps = camps.Analyze(snapshot),
            Structures = structures.Analyze(snapshot),
            Finish = finish.Analyze(snapshot),
            Comeback = comeback.Analyze(snapshot),
            Builds = builds.Analyze(snapshot),
            Focus = focusAnalyzer.Analyze(snapshot, focus),
            Bursts = bursts.Analyze(snapshot),
            Objectives = objectives.Analyze(snapshot),
            Awards = awards.Analyze(snapshot),
            Bosses = bosses.Analyze(snapshot),
        };
    }
}
