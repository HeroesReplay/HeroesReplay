using System.Collections.Generic;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;
using static HeroesReplay.Tests.Unit.YouTube.Metadata.StatHookPickerTests;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>Title stories from the players' Heroes Profile history before the match.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PlayerStoryTests
{
    private static readonly PlayerStorySettings On = new() { Enabled = true };

    [Fact]
    public void Pick_ALongStreakBeatsASmallClimb()
    {
        PlayerStory story = PlayerStoryPicker.Pick(
            new[]
            {
                Facts(0, "Kerrigan", new PlayerHistory { Games = 40, Streak = 9 }),
                Facts(1, "Valla", new PlayerHistory { Games = 40, MmrChange = 32, MmrGames = 20 }),
            },
            replayId: 0,
            namedHeroes: null,
            On
        );

        Assert.Equal(PlayerStoryKind.WinStreak, story.Kind);
        Assert.Equal("Kerrigan rides a 9-game win streak", story.Title);
        Assert.Equal(
            "Story: Blue's Kerrigan came into this game on 9 Storm League wins in a row.",
            story.Line
        );
    }

    [Fact]
    public void Pick_RotatesTheWordingByReplayId()
    {
        PlayerStoryFacts[] players = { Facts(1, "Valla", new PlayerHistory { Games = 30, Streak = -6 }) };

        string first = PlayerStoryPicker.Pick(players, 0, null, On).Title;
        string second = PlayerStoryPicker.Pick(players, 1, null, On).Title;

        Assert.Equal("Valla, 6 losses deep. Redemption arc?", first);
        Assert.Equal("Tilt check: Valla after 6 straight losses", second);
    }

    [Fact]
    public void Pick_ACareerMainOrAOneTrick()
    {
        PlayerStory main = PlayerStoryPicker.Pick(
            new[] { Facts(1, "Murky", new PlayerHistory { Games = 10, HeroGames = 2 }, career: 600) },
            0,
            null,
            On
        );
        PlayerStory trick = PlayerStoryPicker.Pick(
            new[] { Facts(0, "Abathur", new PlayerHistory { Games = 40, HeroGames = 36, HeroWins = 20 }) },
            0,
            null,
            On
        );

        Assert.Equal("600-game Murky main", main.Title);
        Assert.Equal("Story: Red's Murky has played 600 Storm League games on Murky.", main.Line);
        Assert.Equal(PlayerStoryKind.OneTrick, trick.Kind);
        Assert.Equal("The Abathur one-trick", trick.Title);
    }

    [Fact]
    public void Pick_TheWorstHeroLately()
    {
        PlayerStory story = PlayerStoryPicker.Pick(
            new[]
            {
                Facts(
                    1,
                    "Gall",
                    new PlayerHistory { Games = 60, HeroGames = 10, HeroWins = 2, WorstHero = true }
                ),
            },
            0,
            null,
            On
        );

        Assert.Equal(PlayerStoryKind.WorstHero, story.Kind);
        Assert.Equal("Gall: their worst hero lately. Bold.", story.Title);
        Assert.Contains("went 2-8 on Gall over the last 60 days", story.Line);
    }

    [Fact]
    public void Pick_NothingWhenOff_ForANamedHero_OrWithoutAStory()
    {
        PlayerStoryFacts[] players = { Facts(0, "Kerrigan", new PlayerHistory { Games = 40, Streak = 9 }) };

        Assert.Null(PlayerStoryPicker.Pick(players, 0, null, new PlayerStorySettings()));
        Assert.Null(PlayerStoryPicker.Pick(players, 0, new[] { "Kerrigan" }, On));
        Assert.Null(
            PlayerStoryPicker.Pick(
                new[] { Facts(0, "Kerrigan", new PlayerHistory { Games = 40, Streak = 2 }) },
                0,
                null,
                On
            )
        );
    }

    [Fact]
    public void Title_TheStoryLeads_AndItsLineAndTheAttributionGoInTheDescription()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65644212,
                Map = "Cursed Hollow",
                GameMode = "Storm League",
                Rank = "Diamond 2",
                Roster = new ReplayMediaPlayer[] { Blue("Kerrigan"), Red("Valla") },
                IsCompleteRecording = true,
                PlayerStories = new[] { Facts(0, "Kerrigan", new PlayerHistory { Games = 40, Streak = 9 }) },
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings { PlayerStories = On },
            }
        );

        Assert.Equal(
            "Kerrigan rides a 9-game win streak - Cursed Hollow - Storm League - Diamond 2 - 65644212",
            metadata.Title
        );
        Assert.Equal("Kerrigan rides a 9-game win streak", metadata.PlayerStory);
        Assert.Equal(StatHookPicker.Attribution, metadata.DescriptionLines[0]);
        Assert.Contains(
            "Story: Blue's Kerrigan came into this game on 9 Storm League wins in a row.",
            metadata.DescriptionLines
        );
    }

    [Fact]
    public void Title_WithoutStoriesStaysAsItWas()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65644212,
                Map = "Cursed Hollow",
                GameMode = "Storm League",
                Rank = "Diamond 2",
                Roster = new ReplayMediaPlayer[] { Blue("Kerrigan"), Red("Valla") },
                PlayerStories = new[] { Facts(0, "Kerrigan", new PlayerHistory { Games = 40, Streak = 9 }) },
            },
            new FullMatchMetadataOptions { Titles = new YouTubeTitleSettings() }
        );

        Assert.Equal("Cursed Hollow - Storm League - Diamond 2 - 65644212", metadata.Title);
        Assert.Null(metadata.PlayerStory);
        Assert.DoesNotContain(StatHookPicker.Attribution, metadata.DescriptionLines);
    }

    [Theory]
    [InlineData(0, "Patch powerhouse Kerrigan")]
    [InlineData(1, "Kerrigan OP, nerf Chen")]
    [InlineData(2, "Busted? Top-3 Kerrigan this patch")]
    public void CommunityWording_RotatesThePatchExtremes(int seed, string expected) =>
        Assert.Equal(expected, StatHookPicker.ExtremeTitle("Kerrigan", true, true, seed));

    [Fact]
    public void CommunityWording_ChenGetsTheOriginalJoke_AndPlainWordingIsUnchanged()
    {
        Assert.Equal("Chen OP. Nerf Chen. (For real)", StatHookPicker.ExtremeTitle("Chen", true, true, 1));
        Assert.Equal("Underdog Nova", StatHookPicker.ExtremeTitle("Nova", false, false, 1));
        Assert.Equal("Off-meta Nova", StatHookPicker.ExtremeTitle("Nova", false, true, 1));
    }

    private static PlayerStoryFacts Facts(int team, string hero, PlayerHistory history, int? career = null)
    {
        history.Status = PlayerHistoryStatus.Known;
        return new PlayerStoryFacts(team, hero, history, career);
    }
}
