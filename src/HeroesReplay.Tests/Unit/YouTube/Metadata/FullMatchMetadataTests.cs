using System;
using System.Collections.Generic;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FullMatchMetadataTests
{
    private static readonly DateTime When = new DateTime(2026, 9, 28, 18, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void TwoReplays_ProduceDifferentTitlesAndDescriptions()
    {
        FullMatchMetadata ordinary = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 111,
                GameDateUtc = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
                GameVersion = "2.57.0.98285",
                Map = "Dragon Shire",
                GameMode = "Storm League",
                Rank = "Diamond 3",
                AverageMmr = 2200,
                FocusHero = "Li-Ming",
                IsCompleteRecording = false,
            },
            new FullMatchMetadataOptions()
        );
        FullMatchMetadata requested = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 222,
                GameDateUtc = new DateTime(2026, 9, 2, 21, 15, 0, DateTimeKind.Utc),
                GameVersion = "2.57.0.98285",
                Map = "Alterac Pass",
                GameMode = "Storm League",
                Rank = "Master",
                AverageMmr = 3100,
                FocusHero = "Greymane",
                NamedPlayer = true,
                RecordAndUpload = true,
                RequestedBy = "ViewerZZ",
                IsCompleteRecording = true,
                NotableEvents = new[] { Pentakill("Greymane", 80) },
            },
            new FullMatchMetadataOptions()
        );

        Assert.NotEqual(ordinary.Title, requested.Title);
        Assert.NotEqual(ordinary.Description, requested.Description);
        Assert.Equal("Dragon Shire - Storm League - Diamond 3 - 111", ordinary.Title);
        Assert.DoesNotContain("Li-Ming", ordinary.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("MMR", ordinary.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pentakill", ordinary.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full match", ordinary.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("222", ordinary.Description);
        Assert.DoesNotContain(
            "pentakill",
            ordinary.Description,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("ViewerZZ", ordinary.Description);
        Assert.DoesNotContain("Full match.", ordinary.Description);
        Assert.Equal(
            "Greymane focus - Alterac Pass - Storm League - Master - 222",
            requested.Title
        );
        Assert.DoesNotContain("Pentakill", requested.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full match", requested.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MMR", requested.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replayID=222", requested.Description);
        Assert.Contains("2026-09-02 21:15", requested.Description);
        Assert.Contains("Greymane pentakill", requested.Description);
        Assert.Contains("Requested by: ViewerZZ", requested.Description);
        Assert.Contains("Full match.", requested.Description);
        Assert.DoesNotContain("MMR", requested.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepeatBuild_IsDeterministic()
    {
        FullMatchMetadataInput input = Ordinary();
        FullMatchMetadata first = FullMatchMetadataBuilder.Build(input, null);
        FullMatchMetadata second = FullMatchMetadataBuilder.Build(input, null);

        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.Tags, second.Tags);
        Assert.Equal(FullMatchMetadataBuilder.TemplateVersion, first.TemplateVersion);
        Assert.Equal("7", first.TemplateVersion);
    }

    [Fact]
    public void OrdinaryFacts_KeepReplayIdCategoryRosterLinkAndMode()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Ordinary(),
            new FullMatchMetadataOptions { CategoryId = "20" }
        );

        Assert.Equal(65389750, metadata.ReplayId);
        Assert.Equal(
            "https://www.heroesprofile.com/Match/Single/?replayID=65389750",
            metadata.HeroesProfileUrl
        );
        Assert.Equal("20", metadata.CategoryId);
        Assert.Equal("Dragon Shire", metadata.Map);
        Assert.Equal("Storm League", metadata.GameMode);
        Assert.Equal("Diamond 3", metadata.Rank);
        Assert.Contains("Replay ID: 65389750", metadata.DescriptionLines);
        Assert.Contains(
            "Heroes Profile: https://www.heroesprofile.com/Match/Single/?replayID=65389750",
            metadata.DescriptionLines
        );
        Assert.Contains("Mode: Storm League", metadata.DescriptionLines);
        Assert.Contains("Rank: Diamond 3", metadata.DescriptionLines);
        Assert.Contains("Build: 2.57.0.98285", metadata.DescriptionLines);
        Assert.Contains("Date: 2026-09-28 18:05 UTC", metadata.DescriptionLines);
        Assert.Contains("Featured: Li-Ming", metadata.DescriptionLines);
        Assert.Contains("65389750", metadata.Title);
        Assert.DoesNotContain("Hashtags:", metadata.Description);
        Assert.DoesNotContain("#HeroesOfTheStorm", metadata.Description);
        Assert.Contains("Dragon Shire", metadata.Tags);
        Assert.Contains("Storm League", metadata.Tags);
        Assert.Contains("Diamond", metadata.Tags);
        Assert.Contains("Heroes of the Storm", metadata.Tags);
    }

    [Fact]
    public void MissingOptionalFields_OmitPlaceholders()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput { ReplayId = 42 },
            null
        );

        Assert.Equal("42", metadata.Title);
        Assert.Contains("Replay ID: 42", metadata.Description);
        Assert.Contains("replayID=42", metadata.HeroesProfileUrl);
        Assert.DoesNotContain("Rank:", metadata.Description);
        Assert.DoesNotContain("Average MMR:", metadata.Description);
        Assert.DoesNotContain("Featured:", metadata.Description);
        Assert.DoesNotContain("Highlights:", metadata.Description);
        Assert.DoesNotContain("Requested by:", metadata.Description);
        Assert.DoesNotContain("Result:", metadata.Description);
        Assert.DoesNotContain("Build:", metadata.Description);
        Assert.DoesNotContain("Map:", metadata.Description);
        Assert.DoesNotContain("Mode:", metadata.Description);
        Assert.DoesNotContain("Date:", metadata.Description);
        Assert.DoesNotContain(
            "Full match",
            metadata.Description,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("Unknown", metadata.Description);
        Assert.DoesNotContain("N/A", metadata.Description);
        Assert.Null(metadata.Map);
        Assert.Null(metadata.Rank);
        Assert.Null(metadata.AverageMmr);
        Assert.Null(metadata.FocusHero);
        Assert.False(metadata.ClaimsFullMatch);
        Assert.False(metadata.ClaimsPentakill);
        Assert.False(metadata.ClaimsTeamWipe);
        Assert.False(metadata.IncludesSpoiler);
    }

    [Fact]
    public void ReplayIdAlone_KeepsTheHeroesProfileSlot()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput { ReplayId = 42 },
            null
        );

        Assert.Equal(
            "https://www.heroesprofile.com/Match/Single/?replayID=42",
            metadata.HeroesProfileUrl
        );
        Assert.Contains("Heroes of the Storm", metadata.Tags);
        Assert.Equal("20", metadata.CategoryId);
    }

    [Fact]
    public void EmptyAndNullInput_MakeNoClaims()
    {
        FullMatchMetadata empty = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput(),
            null
        );
        FullMatchMetadata missing = FullMatchMetadataBuilder.Build(null, null);

        Assert.Equal(string.Empty, empty.Title);
        Assert.Equal(string.Empty, empty.Description);
        Assert.Empty(empty.Tags);
        Assert.False(empty.ClaimsFullMatch);
        Assert.False(missing.ClaimsPentakill);
        Assert.False(missing.ClaimsTeamWipe);
        Assert.Equal("7", missing.TemplateVersion);
        Assert.Null(missing.ReplayId);
        Assert.Null(missing.HeroesProfileUrl);
    }

    [Fact]
    public void CompleteRecording_SaysFullMatch_AndIncompleteDoesNot()
    {
        FullMatchMetadataInput incomplete = Ordinary() with { IsCompleteRecording = false };
        FullMatchMetadataInput complete = Ordinary() with { IsCompleteRecording = true };

        FullMatchMetadata hidden = FullMatchMetadataBuilder.Build(incomplete, null);
        FullMatchMetadata shown = FullMatchMetadataBuilder.Build(complete, null);

        Assert.False(hidden.ClaimsFullMatch);
        Assert.DoesNotContain("full match", hidden.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full match", hidden.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(shown.ClaimsFullMatch);
        Assert.DoesNotContain("full match", shown.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Dragon Shire - Storm League - Diamond 3 - 65389750", shown.Title);
        Assert.Contains("Full match.", shown.DescriptionLines);
    }

    [Fact]
    public void RequestAttribution_IsIncludedOnlyWhenPermitted()
    {
        FullMatchMetadataInput input = Ordinary() with
        {
            RecordAndUpload = true,
            RequestedBy = "ViewerZZ",
        };

        FullMatchMetadata shown = FullMatchMetadataBuilder.Build(
            input,
            new FullMatchMetadataOptions { IncludeRequestAttribution = true }
        );
        FullMatchMetadata hidden = FullMatchMetadataBuilder.Build(
            input,
            new FullMatchMetadataOptions { IncludeRequestAttribution = false }
        );

        Assert.Equal("Dragon Shire - Storm League - Diamond 3 - 65389750", shown.Title);
        Assert.DoesNotContain("Li-Ming", shown.Title, StringComparison.Ordinal);
        Assert.Contains("Requested by: ViewerZZ", shown.Description);
        Assert.Equal("Dragon Shire - Storm League - Diamond 3 - 65389750", hidden.Title);
        Assert.DoesNotContain("Requested", hidden.Title);
        Assert.DoesNotContain("ViewerZZ", hidden.Description);
    }

    [Fact]
    public void SpectateRequest_IsNotAttributed()
    {
        FullMatchMetadataInput input = Ordinary() with
        {
            RecordAndUpload = false,
            RequestedBy = "ViewerZZ",
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.DoesNotContain("Requested", metadata.Title);
        Assert.DoesNotContain("ViewerZZ", metadata.Description);
    }

    [Fact]
    public void HighRank_IncludesRankAndRoundedMmr()
    {
        FullMatchMetadataInput input = Ordinary() with { Rank = "Master 1", AverageMmr = 3120.5 };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.Contains("Rank: Master 1", metadata.DescriptionLines);
        Assert.DoesNotContain("MMR", metadata.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MMR", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3121, metadata.AverageMmr);
        Assert.Contains("Master", metadata.Tags);
        Assert.Contains("Master 1", metadata.Title);
    }

    [Fact]
    public void NegativeMmr_IsOmitted()
    {
        FullMatchMetadataInput input = Ordinary() with { AverageMmr = -4 };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.Null(metadata.AverageMmr);
        Assert.DoesNotContain("Average MMR:", metadata.Description);
    }

    [Fact]
    public void PentakillAndTeamWipe_RequireParsedEvidence()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Li-Ming", "Artanis"),
                Death(103, "Li-Ming", "Butcher"),
                Death(106, "Li-Ming", "Chromie"),
                Death(109, "Li-Ming", "Diablo"),
                Death(112, "Li-Ming", "E.T.C."),
            }
        );
        FullMatchMetadataInput input = Ordinary() with
        {
            IsCompleteRecording = false,
            NotableEvents = clips,
            FocusHero = "Johanna",
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.True(metadata.ClaimsPentakill);
        Assert.True(metadata.ClaimsTeamWipe);
        Assert.False(metadata.ClaimsFullMatch);
        Assert.DoesNotContain("Pentakill", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("team wipe", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Li-Ming", metadata.Title, StringComparison.Ordinal);
        Assert.Equal("Dragon Shire - Storm League - Diamond 3 - 65389750", metadata.Title);
        Assert.Contains("Li-Ming pentakill", metadata.Description);
        Assert.Contains("Featured: Johanna", metadata.DescriptionLines);
        Assert.Contains("Pentakill", metadata.Tags);
        Assert.Contains("Team wipe", metadata.Tags);
        Assert.DoesNotContain("Li-Ming pentakill (team wipe)", metadata.Description);
    }

    [Fact]
    public void TeamWipeWithoutPentakill_DoesNotClaimPentakill()
    {
        IReadOnlyList<TeamKillClip> clips = new[]
        {
            new TeamKillClip(TeamKillClips.TeamWipeKind, "Artanis", 100, 108, 88, 116, "team wipe"),
        };
        FullMatchMetadataInput input = Ordinary() with { NotableEvents = clips };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.True(metadata.ClaimsTeamWipe);
        Assert.False(metadata.ClaimsPentakill);
        Assert.DoesNotContain("team wipe", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("team wipe", metadata.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pentakill", metadata.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "pentakill",
            metadata.Description,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            metadata.Tags,
            tag => tag.Equals("Pentakill", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void UnverifiedEventText_IsNotAClaim()
    {
        FullMatchMetadataInput input = Ordinary() with
        {
            NotableEvents = new[]
            {
                new TeamKillClip(
                    "highlight",
                    "Nova",
                    1,
                    2,
                    1,
                    2,
                    "unverified-event-claim pentakill"
                ),
            },
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.False(metadata.ClaimsPentakill);
        Assert.False(metadata.ClaimsTeamWipe);
        Assert.DoesNotContain("unverified-event-claim", metadata.Title);
        Assert.DoesNotContain("unverified-event-claim", metadata.Description);
        Assert.DoesNotContain("Highlights:", metadata.Description);
        Assert.DoesNotContain("pentakill", metadata.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyEvidence_FeedsMetadataWithoutInventingEvents()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Li-Ming", "Artanis"),
                Death(103, "Li-Ming", "Butcher"),
                Death(106, "Li-Ming", "Chromie"),
                Death(109, "Li-Ming", "Diablo"),
                Death(112, "Li-Ming", "E.T.C."),
            }
        );
        var settings = new ReplayMediaPolicySettings
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.Selected,
            PublicationMode = ReplayPublicationMode.Curated,
            RequireCurrentPatch = true,
            MinimumGameVersion = "2.57.0.98285",
            MinimumHighSkillRank = "Master",
        };
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = 65389750,
                GameDateUtc = When,
                GameVersion = "2.57.0.98285",
                Map = "Dragon Shire",
                GameMode = "Storm League",
                Rank = "Diamond",
                NotableEvents = clips,
                Completion = new ReplayMediaCompletion { IsVerifiedComplete = true },
                Media = new ReplayMediaFinalization { IsFinalized = true, IsCorrelated = true },
            },
            settings,
            When
        );

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = decision.ReplayId,
                Map = decision.Map,
                GameMode = decision.GameMode,
                NotableEvents = decision.NotableEvents,
                IsCompleteRecording = false,
            },
            null
        );

        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
        Assert.True(metadata.ClaimsPentakill);
        Assert.True(metadata.ClaimsTeamWipe);
        Assert.False(metadata.ClaimsFullMatch);
        Assert.DoesNotContain(
            "full match",
            metadata.Description,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Spoiler_IsOffUnlessConfigured()
    {
        FullMatchMetadataInput input = Ordinary() with { Winner = "ZZZWINNER" };

        FullMatchMetadata hidden = FullMatchMetadataBuilder.Build(
            input,
            new FullMatchMetadataOptions { IncludeSpoilers = false }
        );
        FullMatchMetadata shown = FullMatchMetadataBuilder.Build(
            input,
            new FullMatchMetadataOptions { IncludeSpoilers = true }
        );

        Assert.False(hidden.IncludesSpoiler);
        Assert.DoesNotContain("ZZZWINNER", hidden.Title);
        Assert.DoesNotContain("ZZZWINNER", hidden.Description);
        Assert.DoesNotContain("Result:", hidden.Description);
        Assert.True(shown.IncludesSpoiler);
        Assert.Contains("Result: ZZZWINNER", shown.DescriptionLines);
        Assert.DoesNotContain("ZZZWINNER", shown.Title);
    }

    [Fact]
    public void NonEnglishMap_UsesTheEnglishName()
    {
        FullMatchMetadata korean = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65396086,
                Map = "용의 둥지",
                HeroesProfileMap = "용의 둥지",
                MapAlternativeName = "DragonShire",
                GameMode = "Storm League",
            },
            null
        );
        FullMatchMetadata french = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65396084,
                Map = "Le laboratoire de Braxis",
                HeroesProfileMap = "Le laboratoire de Braxis",
                MapAlternativeName = "BraxisHoldout",
            },
            null
        );

        Assert.Equal("Dragon Shire", korean.Map);
        Assert.Contains("Dragon Shire", korean.Title);
        Assert.Contains("Dragon Shire", korean.Tags);
        Assert.DoesNotContain("용", korean.Title);
        Assert.DoesNotContain("용", korean.Description);
        Assert.DoesNotContain(korean.Tags, tag => tag.Contains("용"));
        Assert.Equal("Braxis Holdout", french.Map);
        Assert.Contains("Braxis Holdout", french.Title);
        Assert.DoesNotContain("laboratoire", french.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roster_ListsEachTeamsHeroesWithoutBattleTagNumbers()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 42,
                Roster = new[]
                {
                    new ReplayMediaPlayer
                    {
                        Team = 0,
                        Hero = "Li-Ming",
                        Name = "Salty",
                        BattleTag = 111,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 0,
                        Hero = "Johanna",
                        Name = "Already#9",
                        BattleTag = 999,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 0,
                        Hero = "HeroLiMing",
                        Name = "Attr",
                        BattleTag = 4,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 2,
                        Hero = "Abathur",
                        Name = "Watcher",
                        BattleTag = 1,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 1,
                        Hero = "Lunara",
                        Name = "Elite",
                        BattleTag = 55,
                        IsAi = true,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 1,
                        Hero = "Muradin",
                        Name = "Plain",
                        BattleTag = 0,
                    },
                    new ReplayMediaPlayer
                    {
                        Team = 1,
                        Name = "NoHero",
                        BattleTag = 50,
                    },
                    new ReplayMediaPlayer { Team = 1, Hero = "Illidan" },
                    new ReplayMediaPlayer { Team = 0 },
                },
            },
            null
        );

        Assert.Contains(
            "Blue: Li-Ming (Salty), Johanna (Already), HeroLiMing (Attr)",
            metadata.DescriptionLines
        );
        Assert.Contains(
            "Red: Lunara (AI), Muradin (Plain), NoHero, Illidan",
            metadata.DescriptionLines
        );
        Assert.DoesNotContain("Abathur", metadata.Description);
        Assert.DoesNotContain("#55", metadata.Description);
        Assert.DoesNotContain("#0", metadata.Description);
    }

    [Fact]
    public void Tags_AreDedupedIgnoringCase()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 7,
                Map = "Dragon Shire",
                GameMode = "dragon shire",
                FocusHero = "Dragon Shire",
            },
            null
        );

        Assert.Equal(new[] { "Dragon Shire", "Heroes of the Storm" }, metadata.Tags);
    }

    [Fact]
    public void LocalGameDate_IsOmitted()
    {
        FullMatchMetadataInput input = Ordinary() with
        {
            GameDateUtc = new DateTime(2026, 9, 28, 18, 5, 0, DateTimeKind.Local),
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, null);

        Assert.DoesNotContain("Date:", metadata.Description);
        Assert.DoesNotContain("2026-09-28", metadata.Title);
    }

    [Fact]
    public void IllegalCharacters_AreStripped()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 9,
                Map = "Dragon<Shire>",
                GameMode = "Storm<League>",
                FocusHero = "Li<Ming>",
                RequestedBy = "A<B>",
                RecordAndUpload = true,
            },
            null
        );

        Assert.DoesNotContain("<", metadata.Title);
        Assert.DoesNotContain(">", metadata.Title);
        Assert.DoesNotContain("<", metadata.Description);
        Assert.DoesNotContain(">", metadata.Description);
        Assert.All(metadata.Tags, tag => Assert.DoesNotContain("<", tag));
        Assert.All(metadata.Tags, tag => Assert.DoesNotContain(">", tag));
        Assert.Contains("Requested by: AB", metadata.Description);
    }

    [Fact]
    public void CustomCategory_IsPreserved()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Ordinary(),
            new FullMatchMetadataOptions { CategoryId = "24" }
        );

        Assert.Equal("24", metadata.CategoryId);
    }

    [Fact]
    public void MaximumLength_StaysWithinYouTubeLimits()
    {
        var roster = new List<ReplayMediaPlayer>();
        for (int i = 0; i < 300; i++)
        {
            roster.Add(
                new ReplayMediaPlayer
                {
                    Team = 0,
                    Hero = new string('H', 80),
                    Name = "P" + i.ToString("000") + new string('N', 60),
                    BattleTag = 1000 + i,
                }
            );
        }

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 123456789,
                GameDateUtc = When,
                GameVersion = new string('B', 80),
                Map = new string('M', 400),
                GameMode = new string('G', 80),
                Rank = new string('R', 80),
                AverageMmr = 2500,
                FocusHero = new string('F', 400),
                Roster = roster,
                IsCompleteRecording = true,
                Winner = new string('W', 200),
                RecordAndUpload = true,
                RequestedBy = new string('Q', 80),
                NotableEvents = new[] { Pentakill(new string('K', 80), 4) },
            },
            new FullMatchMetadataOptions { IncludeSpoilers = true }
        );

        Assert.InRange(metadata.Title.Length, 1, FullMatchMetadataBuilder.TitleMaxCharacters);
        Assert.InRange(
            metadata.Description.Length,
            1,
            FullMatchMetadataBuilder.DescriptionMaxCharacters
        );
        Assert.Contains("123456789", metadata.Title);
        Assert.Contains("replayID=123456789", metadata.HeroesProfileUrl);
        Assert.Contains("Full match.", metadata.Description);
        Assert.DoesNotContain("P299", metadata.Description);
        Assert.All(
            metadata.Tags,
            tag => Assert.InRange(tag.Length, 1, FullMatchMetadataBuilder.TagMaxCharacters)
        );
        Assert.InRange(
            string.Join(",", metadata.Tags).Length,
            0,
            FullMatchMetadataBuilder.TagsMaxCharacters
        );
        Assert.DoesNotContain("<", metadata.Title);
        Assert.DoesNotContain(">", metadata.Description);
    }

    private static FullMatchMetadataInput Ordinary()
    {
        return new FullMatchMetadataInput
        {
            ReplayId = 65389750,
            GameDateUtc = When,
            GameVersion = "2.57.0.98285",
            Map = "Dragon Shire",
            GameMode = "Storm League",
            Rank = "Diamond 3",
            AverageMmr = 2500,
            FocusHero = "Li-Ming",
            IsCompleteRecording = false,
        };
    }

    private static TeamKillClip Pentakill(string hero, int second)
    {
        return new TeamKillClip(
            TeamKillClips.PentakillKind,
            hero,
            second,
            second + 4,
            second,
            second + 8,
            "untrusted description"
        );
    }

    private static TeamKillDeath Death(int second, string killer, string victim)
    {
        return new TeamKillDeath(second, killer, victim);
    }
}
