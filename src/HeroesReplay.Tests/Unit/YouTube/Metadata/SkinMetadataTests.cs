using System;
using System.Linq;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Tests.Unit.HeroesData;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SkinMetadataTests
{
    private static readonly DateTime Played = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SkinCatalog Skins = SkinCatalog.Read(
        SkinCatalogTests.SkinData,
        SkinCatalogTests.GameStrings
    );

    private static readonly ReplayMediaPlayer[] Roster =
    {
        new() { Team = 0, Hero = "Mephisto", Name = "Cow", BattleTag = 1111, Skin = "MephistoMoophistoVar1" },
        new() { Team = 0, Hero = "Tyrael", Name = "Angel", BattleTag = 2222, Skin = "TyraelMecha" },
        new() { Team = 0, Hero = "Abathur", Name = "Slug", BattleTag = 3333, Skin = "MephistoVar1" },
        new() { Team = 1, Hero = "Valla", Name = "Hunter", BattleTag = 4444 },
        new() { Team = 1, Hero = "Mephisto", Name = "Hate", BattleTag = 5555, Skin = "MephistoVar1" },
    };

    private static FullMatchMetadata Build(HeroFocusFacts focus, string focusPlayer, SkinSettings settings = null) =>
        FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 66812345,
                GameDateUtc = Played,
                Map = "Tomb of the Spider Queen",
                GameMode = "Storm League",
                Rank = "Diamond 2",
                Roster = Roster,
                HeroFocus = focus,
                HeroFocusKda = focus == null ? null : "12/2/8",
                HeroFocusPlayer = focusPlayer,
                Skins = Skins,
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings { Skins = settings ?? new SkinSettings() },
            }
        );

    [Fact]
    public void FocusVideo_NamesItsHerosNewSkinInTheTitle_BaseNameOnly()
    {
        FullMatchMetadata metadata = Build(
            new HeroFocusFacts("Mephisto", "Meph", "Ranged Assassin", null),
            "Cow#1111"
        );

        Assert.StartsWith("Mephisto gameplay - Moophisto skin - 12/2/8 - ", metadata.Title);
        Assert.DoesNotContain("Holy Holstein", metadata.Title);
        Assert.EndsWith("66812345", metadata.Title);
        Assert.Contains("Moophisto", metadata.Tags);
    }

    [Fact]
    public void FocusVideo_OldNotableSkin_StaysOutOfTheTitle()
    {
        FullMatchMetadata metadata = Build(
            new HeroFocusFacts("Tyrael", "Tyrl", "Bruiser", null),
            "Angel#2222"
        );

        Assert.DoesNotContain("skin", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mecha Tyrael", metadata.Tags);
        Assert.Contains("Moophisto", metadata.Tags);
    }

    [Fact]
    public void OtherVideos_NeverNameSkinsInTheTitle()
    {
        FullMatchMetadata metadata = Build(null, null);

        Assert.DoesNotContain("Moophisto", metadata.Title);
        Assert.Contains(
            "Blue skins: Holy Holstein Moophisto (Mephisto), Mecha Tyrael",
            metadata.DescriptionLines
        );
    }

    [Fact]
    public void Description_ListsOnlyNotableSkins_AsTheLastLines()
    {
        FullMatchMetadata metadata = Build(null, null);

        Assert.Equal(
            "Blue skins: Holy Holstein Moophisto (Mephisto), Mecha Tyrael",
            metadata.DescriptionLines.Last()
        );
        Assert.DoesNotContain(metadata.DescriptionLines, line => line.StartsWith("Red skins"));
        Assert.DoesNotContain("Hollow Mephisto", metadata.Description);
    }

    [Fact]
    public void Disabled_NamesNoSkins()
    {
        FullMatchMetadata metadata = Build(
            new HeroFocusFacts("Mephisto", "Meph", "Ranged Assassin", null),
            "Cow#1111",
            new SkinSettings { Enabled = false }
        );

        Assert.DoesNotContain("skin", metadata.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skin", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Moophisto", metadata.Tags);
    }

    [Theory]
    [InlineData(105, "Lightning Nova Mephisto gameplay - Moophisto skin - Tomb of the Spider Queen - Storm League - 66812345")]
    [InlineData(90, "Lightning Nova Mephisto gameplay - Tomb of the Spider Queen - Storm League - 66812345")]
    public void FocusTitle_DropsTheScoreThenTheSkinBeforeTheBuild(int max, string expected)
    {
        string title = FocusTitle.Compose(
            max,
            "Mephisto",
            "Lightning Nova Mephisto",
            "12/2/8",
            "Tomb of the Spider Queen",
            "Storm League",
            "Diamond 2",
            null,
            "66812345",
            "Moophisto skin"
        );

        Assert.Equal(expected, title);
    }
}
