using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;
using static HeroesReplay.Tests.Unit.YouTube.Metadata.TeamCompositionTests;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchDraftTests
{
    private static readonly string[] Standard = { "Johanna", "Chen", "Rehgar", "Valla", "Raynor" };
    private static readonly string[] SplitPush =
    {
        "Johanna",
        "Chen",
        "Nazeebo",
        "Zagara",
        "Rehgar",
    };
    private static readonly string[] Dive =
    {
        "Muradin",
        "Thrall",
        "Falstad",
        "Genji",
        "Brightwing",
    };

    // Double bruiser without a tank: 14.9% of corpus games.
    private static readonly string[] DoubleBruiser =
    {
        "Chen",
        "Thrall",
        "Rehgar",
        "Valla",
        "Raynor",
    };

    // No healer (1.4%), and four pushers or clearers, so Siege (6.1%).
    private static readonly string[] NoHealerSiege =
    {
        "Johanna",
        "Chen",
        "Valla",
        "Raynor",
        "Jaina",
    };
    private static readonly string[] MercControl =
    {
        "Johanna",
        "Sonya",
        "Anduin",
        "Illidan",
        "Valla",
    };

    [Fact]
    public void Read_NamesOneTeamsLabelWithTheTeam()
    {
        MatchDraft draft = Read(SplitPush, Standard);

        Assert.Equal("Blue split push", draft.Line);
        Assert.Equal("Blue split push", draft.Title);
        Assert.Equal(new[] { "Split push" }, draft.Labels);
    }

    [Fact]
    public void Read_WritesALabelBothTeamsShareOnce()
    {
        MatchDraft draft = Read(SplitPush, SplitPush);

        Assert.Equal("Split push", draft.Line);
        Assert.Equal("Split push", draft.Title);
        Assert.Equal(new[] { "Split push" }, draft.Labels);
    }

    [Fact]
    public void Read_KeepsADraftNoteWithoutLabelsAsBefore()
    {
        MatchDraft draft = Read(DoubleBruiser, Standard);

        Assert.Equal("Blue double bruiser", draft.Line);
        Assert.Equal("Blue double bruiser", draft.Title);
        Assert.Empty(draft.Labels);
        Assert.Null(Read(Standard, Standard).Line);
        Assert.Null(Read(Standard, Standard).Title);
    }

    [Fact]
    public void Read_ARarerLabelTakesTheTitleFromACommonDraftNote()
    {
        MatchDraft draft = Read(DoubleBruiser, Dive);

        Assert.Equal("Blue double bruiser, Red dive", draft.Line);
        Assert.Equal("Red dive", draft.Title);
        Assert.Equal(new[] { "Dive" }, draft.Labels);
    }

    [Fact]
    public void Read_ARarerDraftNoteKeepsTheTitleAndTheRarestLabelIsTheOnlyCandidate()
    {
        MatchDraft draft = Read(NoHealerSiege, MercControl);

        Assert.Equal("Blue no healer, Blue siege, Red merc control", draft.Line);
        Assert.Equal("Blue no healer", draft.Title);
        Assert.Equal(new[] { "Siege", "Merc control" }, draft.Labels);

        var titles = new YouTubeTitleSettings { DraftNotes = false };
        Assert.Equal("Blue siege", Read(titles, NoHealerSiege, MercControl).Title);
    }

    [Fact]
    public void Read_HidesALabelThatIsNotRareEnough()
    {
        var titles = new YouTubeTitleSettings();
        titles.Compositions.Frequencies[TeamComposition.Dive] = 0.25;

        MatchDraft draft = Read(titles, DoubleBruiser, Dive);

        Assert.Equal("Blue double bruiser", draft.Line);
        Assert.Equal("Blue double bruiser", draft.Title);
        Assert.Empty(draft.Labels);
    }

    [Fact]
    public void Read_StaysQuietWhenCompositionsAreOff()
    {
        var titles = new YouTubeTitleSettings();
        titles.Compositions.Enabled = false;

        Assert.Null(Read(titles, SplitPush, Dive).Line);
        Assert.Equal("Blue double bruiser", Read(titles, DoubleBruiser, Dive).Title);
    }

    [Fact]
    public void Build_PutsTheLabelInTheTitleDescriptionAndTags()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Input(Roster(DoubleBruiser, Dive), "Cursed Hollow", "Diamond", null),
            null
        );

        Assert.Equal(
            "Cursed Hollow - Storm League - Diamond - Red dive - 65550001",
            metadata.Title
        );
        Assert.Contains("Draft: Blue double bruiser, Red dive", metadata.DescriptionLines);
        Assert.Contains("Dive", metadata.Tags);
        Assert.Contains("Heroes of the Storm", metadata.Tags);
        Assert.Equal("6", metadata.TemplateVersion);
    }

    [Fact]
    public void Build_DropsTheDraftSlotFirstAtOneHundredCharacters()
    {
        string[] noRanged = { "Johanna", "Illidan", "Zeratul", "The Butcher", "Anduin" };
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Input(
                Roster(noRanged, Standard),
                "Tomb of the Spider Queen",
                "Grandmaster",
                "The Lost Vikings"
            ),
            null
        );

        Assert.Equal(
            "The Lost Vikings focus - Tomb of the Spider Queen - Storm League - Grandmaster - 65550001",
            metadata.Title
        );
        Assert.Contains(
            "Draft: Blue no ranged assassin, Blue triple melee assassin, Blue one ranged, Blue merc control",
            metadata.DescriptionLines
        );
        Assert.Contains("Triple melee assassin", metadata.Tags);
        Assert.Contains("Merc control", metadata.Tags);
    }

    [Fact]
    public void Build_Issue247DraftHasNoTripleSustain()
    {
        string[] blue = { "E.T.C.", "Yrel", "Jaina", "Valla", "Lt. Morales" };
        string[] red = { "Varian", "Sonya", "Li-Ming", "Nazeebo", "Anduin" };
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Input(Roster(blue, red), "Battlefield of Eternity", "Platinum 3", null),
            null
        );

        Assert.DoesNotContain("sustain", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Triple sustain", metadata.Tags);
        Assert.DoesNotContain(
            "sustain",
            metadata.DescriptionLines.First(line => line.StartsWith("Draft:")),
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static MatchDraft Read(string[] blue, string[] red) =>
        Read(new YouTubeTitleSettings(), blue, red);

    private static MatchDraft Read(YouTubeTitleSettings titles, string[] blue, string[] red) =>
        MatchDraft.Read(Catalog, Roster(blue, red), titles);

    private static List<ReplayMediaPlayer> Roster(string[] blue, string[] red)
    {
        List<ReplayMediaPlayer> roster = Team(0, blue);
        roster.AddRange(Team(1, red));
        return roster;
    }

    private static FullMatchMetadataInput Input(
        List<ReplayMediaPlayer> roster,
        string map,
        string rank,
        string focus
    ) =>
        new()
        {
            ReplayId = 65550001,
            Map = map,
            GameMode = "Storm League",
            Rank = rank,
            FocusHero = focus,
            NamedPlayer = focus != null,
            Roster = roster,
            HeroCatalog = Catalog,
        };
}
