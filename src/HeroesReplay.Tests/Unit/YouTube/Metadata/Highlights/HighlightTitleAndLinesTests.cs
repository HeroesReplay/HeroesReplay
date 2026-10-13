using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.YouTube.Metadata.Highlights;
using HeroesReplay.Core.YouTube.Search;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata.Highlights;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HighlightTitleAndLinesTests
{
    private static readonly Highlight Deathless = new(
        HighlightKind.DeathlessCarry,
        "Li-Ming",
        new[]
        {
            "Deathless Teleport Li-Ming 12/0/9",
            "Deathless Li-Ming 12/0/9",
            "Deathless Li-Ming",
        },
        "Deathless"
    );

    [Fact]
    public void Title_LeadsWithTheHighlight_AndKeepsTheReplayIdReadable()
    {
        string title = Compose(100, draft: "Red dive");

        Assert.Equal(
            "Deathless Teleport Li-Ming 12/0/9 - Garden of Terror - Storm League - Master - Red dive - 65581797",
            title
        );
        Assert.True(YouTubeReplayMatch.TryReadTitleId(title, out int id));
        Assert.Equal(65581797, id);
    }

    [Fact]
    public void Title_DropsTheDraftNoteFirst_ThenShortensTheHighlight_ThenGivesUp()
    {
        Assert.Equal(
            "Deathless Teleport Li-Ming 12/0/9 - Garden of Terror - Storm League - Master - 65581797",
            Compose(90, draft: "Red dive")
        );
        Assert.Equal(
            "Deathless Li-Ming 12/0/9 - Garden of Terror - Storm League - Master - 65581797",
            Compose(80, draft: "Red dive")
        );
        Assert.Equal(
            "Deathless Li-Ming - Garden of Terror - Storm League - Master - 65581797",
            Compose(71, draft: "Red dive")
        );
        Assert.Null(Compose(40, draft: null));
    }

    [Fact]
    public void Lines_MergeTheMvpIntoItsStandout_AndNeverSayWhoWon()
    {
        HighlightContext context = HighlightFixtures.Context(
            HighlightFixtures.Insights(comeback: true)
        );

        string highlights = HighlightLines.Highlights(
            context,
            new[] { HighlightFixtures.Pentakill("Valla") }
        );

        Assert.Equal(
            "Highlights: Valla pentakill; Li-Ming 12/0/9 with no deaths; MVP Orphea 7/1/5; insane comeback",
            highlights
        );
        Assert.DoesNotContain("won", highlights, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("victory", highlights, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lines_SoloLanesAndCamps()
    {
        HighlightContext both = HighlightFixtures.Context(
            HighlightFixtures.Insights(blueLane: LaneConfidence.High, redLane: LaneConfidence.High)
        );
        HighlightContext one = HighlightFixtures.Context(
            HighlightFixtures.Insights(blueLane: LaneConfidence.High)
        );
        HighlightContext none = HighlightFixtures.Context(
            HighlightFixtures.Insights(blueLane: LaneConfidence.Medium)
        );

        Assert.Equal("Solo lanes: Muradin (Blue) vs Thrall (Red)", HighlightLines.SoloLanes(both));
        Assert.Equal("Solo lane: Muradin (Blue)", HighlightLines.SoloLanes(one));
        Assert.Null(HighlightLines.SoloLanes(none));
        Assert.Equal(
            "Camps: Blue 9 (4 on Red's side), Red 6 (0 on Blue's side)",
            HighlightLines.Camps(both)
        );
    }

    [Fact]
    public void Lines_ListGoodBuildsWithTheirGuideLinks_UnderAnIcyVeinsHeading()
    {
        IReadOnlyList<string> lines = HighlightLines.TalentBuilds(
            HighlightFixtures.Context(HighlightFixtures.Insights())
        );

        Assert.Equal(
            new[]
            {
                "Talent builds (closest Icy Veins guide build, https://www.icy-veins.com/heroes/):",
                "Li-Ming (Blue): Teleport Build, 6 of 7 talents as recommended - https://www.icy-veins.com/heroes/li-ming-build-guide",
                "Thrall (Red): Chain Lightning Build, 4 of 6 talents as recommended, 2 situational - https://www.icy-veins.com/heroes/thrall-build-guide",
            },
            lines
        );
        Assert.DoesNotContain(
            lines,
            line => line.StartsWith("Build:", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Empty(
            HighlightLines.TalentBuilds(
                HighlightFixtures.Context(
                    HighlightFixtures.Insights(),
                    settings: new HighlightSettings { TalentBuildLines = false }
                )
            )
        );
    }

    private static string Compose(int max, string draft) =>
        HighlightTitle.Compose(
            Deathless,
            max,
            null,
            null,
            "Garden of Terror",
            "Storm League",
            "Master",
            draft,
            "65581797"
        );
}
