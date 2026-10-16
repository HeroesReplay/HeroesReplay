using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Tests.Unit.Support;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Ledger;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusCoverageTests
{
    [Fact]
    public void Build_CountsHeroesRolesAndBuilds_AndListsTheUncovered()
    {
        var ledger = new FocusLedger
        {
            Entries = new List<FocusLedgerEntry>
            {
                Entry("Valla", "Ranged Assassin", Now.AddDays(-3), replayId: 1, key: "1"),
                Entry(
                    "Valla",
                    "Ranged Assassin",
                    Now.AddDays(-2),
                    build: "Burst Build",
                    replayId: 2,
                    key: "2"
                ),
                Entry(
                    "Muradin",
                    "Tank",
                    Now.AddDays(-1),
                    replayId: 3,
                    key: "3",
                    state: FocusEntryState.Picked
                ),
                Entry(
                    "Uther",
                    "Healer",
                    Now,
                    replayId: 4,
                    key: "4",
                    state: FocusEntryState.Abandoned
                ),
            },
        };

        FocusCoverageReport report = FocusCoverage.Build(
            ledger,
            new[] { "Valla", "Muradin", "Uther", "Abathur" },
            new HashSet<int> { 1 }
        );

        Assert.Equal(3, report.Picks);
        Assert.Equal(2, report.Played);
        Assert.Equal(1, report.Published);
        Assert.Equal(4, report.RosterSize);
        Assert.Equal(2, report.HeroesCovered);
        Assert.Equal(new[] { "Abathur", "Uther" }, report.Uncovered);
        Assert.Equal(
            ("Valla", 2, 1),
            (report.Heroes[0].Name, report.Heroes[0].Picks, report.Heroes[0].Published)
        );
        Assert.Equal(0.667, report.Roles.Single(role => role.Name == "Ranged Assassin").Share);
        Assert.Contains(report.Builds, build => build.Name == "Valla: Burst Build");
    }

    [Fact]
    public void PublishedReplayIds_AreTheFullMatchesInTheLibrary()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "hr-focus-library-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string path = Path.Combine(folder, YouTubeLibraryRecord.FileName);
            YouTubeLibraryRecord.Append(
                path,
                new YouTubeLibraryVideo
                {
                    VideoId = "a",
                    ReplayId = 65550003,
                    Kind = YouTubeLibraryRecord.Full,
                }
            );
            YouTubeLibraryRecord.Append(
                path,
                new YouTubeLibraryVideo
                {
                    VideoId = "b",
                    ReplayId = 65550004,
                    Kind = YouTubeLibraryRecord.Clip,
                }
            );
            YouTubeLibraryRecord.Append(
                path,
                new YouTubeLibraryVideo { VideoId = "c", Kind = YouTubeLibraryRecord.Full }
            );

            Assert.Equal(new[] { 65550003 }, FocusCoverage.PublishedReplayIds(path));
            Assert.Empty(FocusCoverage.PublishedReplayIds(Path.Combine(folder, "missing.jsonl")));
        }
        finally
        {
            TestTemp.Delete(folder);
        }
    }
}
