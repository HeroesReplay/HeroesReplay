using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Comeback;
using HeroesReplay.Core.Analysis.Insights.Finish;
using HeroesReplay.Core.Analysis.Insights.Focused;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Analysis.Insights.Structures;

namespace HeroesReplay.Core.Analysis.Insights;

/// <summary>
/// What one replay shows beyond its map and roster (#412): the players' stat lines and
/// standouts, each team's solo laner, camp control, buildings lost, how the match ended, the
/// guide build each player fits, and the hero the spectator followed most. Heroes, talents and
/// builds are English. Written next to the recording as <c>insights.json</c>.
/// </summary>
public sealed record MatchInsights
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public PlayerInsights Players { get; set; } = new();

    public LaneInsights Lanes { get; set; } = new();

    public CampInsights Camps { get; set; } = new();

    public StructureInsights Structures { get; set; } = new();

    public FinishInsights Finish { get; set; } = new();

    public ComebackInsights Comeback { get; set; } = new();

    public BuildInsights Builds { get; set; } = new();

    public FocusInsights Focus { get; set; } = new();
}
