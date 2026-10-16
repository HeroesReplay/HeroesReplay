using System;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>
/// A focus-hero video's title, description and tags (#433): the build and hero lead, the
/// <c>Hero focus:</c> line, never <c>Featured:</c>, and room for the <c>[TEST]</c> marker.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusTitleTests
{
    [Fact]
    public void TheTitle_LeadsWithTheBuildAndHero_ThenTheScore()
    {
        Assert.Equal(
            "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - 65550003",
            FocusTitle.Compose(
                100,
                "Li-Ming",
                "Teleport Li-Ming",
                "12/0/9",
                "Alterac Pass",
                "Storm League",
                "Diamond",
                null,
                "65550003"
            )
        );
        Assert.Equal("Li-Ming gameplay", FocusTitle.Lead("Li-Ming", null));
    }

    [Theory]
    [InlineData(
        200,
        "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - Double healer - 65550003"
    )]
    [InlineData(
        90,
        "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - 65550003"
    )]
    [InlineData(80, "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - 65550003")]
    [InlineData(70, "Teleport Li-Ming gameplay - Alterac Pass - Storm League - 65550003")]
    [InlineData(60, "Li-Ming gameplay - Alterac Pass - Storm League - 65550003")]
    [InlineData(40, null)]
    public void OverTheLimit_TheDraftGoesFirst_ThenTheRank_TheScore_AndTheBuild(
        int limit,
        string expected
    )
    {
        Assert.Equal(
            expected,
            FocusTitle.Compose(
                limit,
                "Li-Ming",
                "Teleport Li-Ming",
                "12/0/9",
                "Alterac Pass",
                "Storm League",
                "Diamond",
                "Double healer",
                "65550003"
            )
        );
    }

    [Fact]
    public void TheLongestTitle_WithTheTestMarker_FitsYouTube()
    {
        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            Focused(
                "The Lost Vikings",
                "Ranged Assassin",
                "Play Me a Long Hymn of Vikings Build",
                map: "Tomb of the Spider Queen",
                rank: "Grandmaster"
            ),
            new YouTubeSettings { TitlePrefix = "[TEST]", PrivacyStatus = "private" }
        );

        Assert.Equal(
            "[TEST] The Lost Vikings gameplay - Tomb of the Spider Queen - Storm League - 65550003",
            entry.Title
        );
        Assert.True(entry.Title.Length <= FullMatchMetadataBuilder.TitleMaxCharacters, entry.Title);
    }

    [Fact]
    public void TheDescription_NamesTheFocus_AndNeverAViewerReview()
    {
        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            Focused("Li-Ming", "Ranged Assassin", "Teleport Build"),
            new YouTubeSettings { PrivacyStatus = "public" }
        );

        Assert.Equal(
            "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - 65550003",
            entry.Title
        );
        Assert.Contains(
            "Hero focus: Li-Ming (Teleport Build), Ranged Assassin",
            entry.DescriptionLines
        );
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line => line.StartsWith("Featured:", StringComparison.Ordinal)
        );
        Assert.Equal(
            new[] { "Li-Ming", "Li-Ming gameplay", "Teleport Li-Ming", "Ranged Assassin" },
            entry.Tags.Skip(3).Take(4)
        );
        Assert.Equal(FullMatchMetadataBuilder.TemplateVersion, entry.TemplateVersion);
    }

    [Fact]
    public void APlainBuild_LeavesTheHeroAlone_AndTheLineStillNamesIt()
    {
        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            Focused("Rexxar", "Bruiser", "Basic Attacks Build"),
            new YouTubeSettings { PrivacyStatus = "public" }
        );

        Assert.StartsWith("Rexxar gameplay - 12/0/9 - ", entry.Title);
        Assert.Contains(
            "Hero focus: Rexxar (Basic Attacks Build), Bruiser",
            entry.DescriptionLines
        );
        Assert.DoesNotContain("Basic Attacks Rexxar", entry.Tags);
    }

    [Fact]
    public void AViewerRequest_KeepsItsOwnTitle()
    {
        LoadedReplay request = Focused("Li-Ming", "Ranged Assassin", "Teleport Build");
        request.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest { Login = "viewer", BattleTag = "Star#1234" },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            request,
            new YouTubeSettings { PrivacyStatus = "public" }
        );

        Assert.DoesNotContain("gameplay", entry.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line => line.StartsWith("Hero focus:", StringComparison.Ordinal)
        );
    }

    private static LoadedReplay Focused(
        string hero,
        string role,
        string build,
        string map = "Alterac Pass",
        string rank = "Diamond"
    )
    {
        Player[] players = Enumerable
            .Range(0, 10)
            .Select(index => new Player
            {
                Name = index == 0 ? "Star" : "Other" + index,
                BattleTag = index == 0 ? 1234 : 2000 + index,
                Team = index < 5 ? 0 : 1,
                PlayerType = PlayerType.Human,
                ScoreResult = new ScoreResult
                {
                    SoloKills = index == 0 ? 12 : 1,
                    Deaths = index == 0 ? 0 : 3,
                    Assists = index == 0 ? 9 : 2,
                },
            })
            .ToArray();
        return new LoadedReplay
        {
            ReplayId = 65550003,
            Replay = new Replay { Players = players },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65550003,
                Map = map,
                GameType = "Storm League",
                Rank = rank,
            },
            FocusPick = new FocusPick(
                65550003,
                "65550003",
                0,
                "Star#1234",
                hero,
                "Attr",
                role,
                build
            ),
        };
    }
}
