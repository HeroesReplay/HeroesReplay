using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>
/// One kind of match insight (#412): reads the replay's facts and returns its own section. Each
/// analyzer is independent, so a new insight is a new analyzer, not a change to the others.
/// </summary>
public interface IInsightAnalyzer<out TInsight>
{
    TInsight Analyze(ReplaySnapshot snapshot);
}
