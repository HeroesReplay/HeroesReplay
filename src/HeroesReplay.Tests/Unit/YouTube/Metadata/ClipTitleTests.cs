using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.BSteps;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Search;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>
/// A clip's title from its own facts: a hook that names the hero and the plain kind word, the map,
/// the replay id as its own part, and the game's name when it fits. Never over 100 characters
/// with the <c>[TEST]</c> marker, the same title for the same clip, and nothing it cannot prove.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClipTitleTests
{
    private const string LongMap = "Tomb of the Spider Queen";

    [Fact]
    public void EveryLine_NamesTheKind_AndNeverTheSeparator()
    {
        foreach (ClipTitle.Line line in ClipTitle.Lines)
        {
            ClipTitle.Facts facts = Everything(line.Kind);
            string text = line.Text(facts);

            Assert.Contains(KindWord(line.Kind), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" - ", text, StringComparison.Ordinal);
            Assert.True(
                line.Emoji.Length == 0
                    || line.Kind is ClipTitle.ClipKind.BStep or ClipTitle.ClipKind.Penta,
                text
            );
            foreach (string word in new[] { "full match", "full game", "insane", "best ever" })
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void EveryClip_FitsWithTheMarker_AndKeepsTheReplayId()
    {
        var titles = new YouTubeTitleSettings();
        foreach (TeamKillClip clip in Clips())
        {
            for (int id = 66698600; id < 66698640; id++)
            {
                string replayId = id.ToString();
                string title = ClipTitle.Compose(clip, LongMap, replayId, titles, "[TEST]");

                Assert.True(title.Length <= 94, title);
                Assert.True(YouTubeReplayMatch.TryReadTitleId(title, out int read), title);
                Assert.Equal(id, read);
                Assert.Contains(LongMap, title, StringComparison.Ordinal);
                Assert.True(Emoji(title) <= 1, title);
                Assert.Equal(title, ClipTitle.Compose(clip, LongMap, replayId, titles, "[TEST]"));
            }
        }
    }

    [Fact]
    public void TheReplayId_RotatesTheLines()
    {
        TeamKillClip quadra = Kill("Kerrigan", 4, 100, 109);
        var seen = new HashSet<string>();
        for (int id = 66698600; id < 66698640; id++)
        {
            seen.Add(Hook(quadra, id));
        }

        Assert.True(seen.Count >= 3, string.Join(" | ", seen));
    }

    [Fact]
    public void ABstepOverAKill_ThenADeath_IsKarma()
    {
        TeamKillClip clip = BStepClips.Clip(
            Step(
                "Sonya",
                new BStepDeath(5, "Muradin", 298, BStepDeathKind.KilledByBstepper, false, 0),
                new BStepDeath(0, "Sonya", 306, BStepDeathKind.Bstepper, true, 6)
            )
        );

        for (int id = 66698600; id < 66698610; id++)
        {
            string hook = Hook(clip, id);
            Assert.Contains("karma", hook, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Muradin", hook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ABstepWithNoKill_NeverNamesAVictim()
    {
        TeamKillClip clip = BStepClips.Clip(
            Step("Valla", new BStepDeath(2, "Uther", 298, BStepDeathKind.Ally, false, 7))
        );

        for (int id = 66698600; id < 66698610; id++)
        {
            string hook = Hook(clip, id);
            Assert.DoesNotContain("Uther", hook, StringComparison.Ordinal);
            Assert.DoesNotContain("karma", hook, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ASteal_WhileBehind_SaysTheLevels()
    {
        var levels = new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>
        {
            [0] = new Dictionary<int, TimeSpan> { [16] = TimeSpan.FromSeconds(500) },
            [1] = new Dictionary<int, TimeSpan> { [13] = TimeSpan.FromSeconds(450) },
        };
        TeamKillClip clip = BossFightClips
            .Select(new BossFightInsights { Fights = new[] { Steal() } }, teamLevels: levels)
            .Single();

        Assert.Equal(3, clip.Facts.LevelsBehind);
        for (int id = 66698600; id < 66698610; id++)
        {
            string hook = Hook(clip, id);
            Assert.Contains("3 levels", hook, StringComparison.Ordinal);
            Assert.Contains("Illidan", hook, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ASteal_WithoutLevels_TellsTheFightInstead()
    {
        TeamKillClip clip = BossFightClips
            .Select(new BossFightInsights { Fights = new[] { Steal() } })
            .Single();

        Assert.Null(clip.Facts.LevelsBehind);
        for (int id = 66698600; id < 66698610; id++)
        {
            Assert.Contains("5 on the boss", Hook(clip, id), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LevelAt_ReadsTheHighestLevelReached()
    {
        var levels = new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>
        {
            [0] = new Dictionary<int, TimeSpan>
            {
                [10] = TimeSpan.FromSeconds(400),
                [11] = TimeSpan.FromSeconds(500),
            },
        };

        Assert.Equal(10, BossFightClips.LevelAt(levels, 0, 450));
        Assert.Equal(11, BossFightClips.LevelAt(levels, 0, 500));
        Assert.Null(BossFightClips.LevelAt(levels, 1, 500));
        Assert.Null(BossFightClips.LevelAt(null, 0, 500));
    }

    [Fact]
    public void Emoji_Off_KeepsTheWords()
    {
        var titles = new YouTubeTitleSettings { Clips = new ClipTitleSettings { Emoji = false } };
        foreach (TeamKillClip clip in Clips())
        {
            Assert.Equal(
                0,
                Emoji(ClipTitle.Compose(clip, "Cursed Hollow", "66698689", titles, null))
            );
        }
    }

    [Fact]
    public void Disabled_WritesTheFlatTitle()
    {
        var titles = new YouTubeTitleSettings
        {
            Clips = new ClipTitleSettings { Enabled = false },
            GameName = new GameNameSettings { Clips = false },
        };

        Assert.Equal(
            "Kerrigan - quadra kill - Cursed Hollow - 66698689",
            ClipTitle.Compose(
                Kill("Kerrigan", 4, 100, 109),
                "Cursed Hollow",
                "66698689",
                titles,
                null
            )
        );
    }

    [Fact]
    public void TheGameName_IsFullWhenItFits_ElseShort_ElseLeftOut()
    {
        var game = new GameNameSettings();
        string shortTitle = "Raynor triple kill - Cursed Hollow - 66698689";
        string longTitle = new string('x', 75) + " - 66698689";
        string fullTitle = new string('x', 89) + " - 66698689";

        Assert.Equal(shortTitle + " - Heroes of the Storm", game.Append(shortTitle, 100));
        Assert.Equal(longTitle + " - HotS", game.Append(longTitle, 100));
        Assert.Equal(fullTitle, game.Append(fullTitle, 100));
        Assert.Equal(
            shortTitle + " - Heroes of the Storm",
            game.Append(shortTitle + " - Heroes of the Storm", 100)
        );
    }

    private static string Hook(TeamKillClip clip, int id) =>
        ClipTitle.Compose(
            clip,
            "Cursed Hollow",
            id.ToString(),
            new YouTubeTitleSettings { GameName = new GameNameSettings { Clips = false } },
            null
        );

    private static IEnumerable<TeamKillClip> Clips()
    {
        foreach (string hero in new[] { "The Lost Vikings", "Murky", "Chromie", "Cho" })
        {
            yield return Kill(hero, 3, 100, 101);
            yield return Kill(hero, 4, 100, 106);
            yield return Kill(hero, 5, 100, 108) with
            {
                Kind = TeamKillClips.PentakillKind,
            };
            yield return Kill(hero, 5, 100, 140) with
            {
                Kind = TeamKillClips.PentakillKind,
            };
            yield return BStepClips.Clip(
                Step(
                    hero,
                    new BStepDeath(
                        5,
                        "Lt. Morales",
                        298,
                        BStepDeathKind.KilledByBstepper,
                        false,
                        0
                    ),
                    new BStepDeath(6, "The Lost Vikings", 299, BStepDeathKind.Enemy, false, 1),
                    new BStepDeath(0, hero, 306, BStepDeathKind.Bstepper, true, 6)
                )
            );
            yield return BStepClips.Clip(
                Step(hero, new BStepDeath(2, "Uther", 298, BStepDeathKind.Ally, false, 7))
            );
        }

        yield return BossFightClips
            .Select(new BossFightInsights { Fights = new[] { Steal() } })
            .Single();
        yield return BossFightClips
            .Select(
                new BossFightInsights
                {
                    Fights = new[] { Steal() with { KillerHero = "Abathur", KillerTeam = 0 } },
                }
            )
            .Single();
        yield return BossFightClips
            .Select(new BossFightInsights { Fights = new[] { Defended() } })
            .Single();
    }

    private static TeamKillClip Kill(string hero, int blows, int first, int last)
    {
        var kills = new List<TeamKillBlow>();
        string[] victims = { "Jaina", "Uther", "Muradin", "Valla", "Diablo", "Li-Ming" };
        for (int i = 0; i < blows; i++)
        {
            kills.Add(new TeamKillBlow(i == blows - 1 ? last : first + i, victims[i]));
        }

        return new TeamKillClip(
            TeamKillClips.TripleKillKind,
            hero,
            first,
            last,
            first - 12,
            last + 8,
            "description",
            kills
        );
    }

    private static BStep Step(string hero, params BStepDeath[] deaths) =>
        new()
        {
            PlayerIndex = 0,
            Team = 0,
            Hero = hero,
            StartSecond = 300,
            EndSecond = 304,
            Cycles = 9,
            Deaths = deaths,
        };

    private static BossFight Steal() =>
        new()
        {
            Team = 1,
            Second = 600,
            StartSecond = 560,
            Starter = 0,
            Side = 0,
            KillerHero = "Illidan",
            KillerTeam = 1,
            Kind = BossFightKind.Steal,
            Teams = new[]
            {
                new BossFightTeam
                {
                    Team = 0,
                    MostHeroes = 5,
                    GroupSeconds = 30,
                },
                new BossFightTeam { Team = 1, MostHeroes = 2 },
            },
        };

    private static BossFight Defended() =>
        new()
        {
            Team = 0,
            Second = 900,
            StartSecond = 860,
            Starter = 0,
            KillerHero = "Johanna",
            KillerTeam = 0,
            TogetherSeconds = 20,
            Kind = BossFightKind.Defended,
            Teams = new[]
            {
                new BossFightTeam { Team = 0, Deaths = 1 },
                new BossFightTeam { Team = 1, Deaths = 3 },
            },
        };

    private static ClipTitle.Facts Everything(ClipTitle.ClipKind kind) =>
        new()
        {
            Kind = kind,
            Hero = "The Lost Vikings",
            KindWord = KindWord(kind),
            Seconds = 6,
            Wiped = true,
            Cycles = 12,
            BStepSeconds = 4,
            Victims = "Lt. Morales and Sgt. Hammer",
            VictimCount = 2,
            DiedAfter = true,
            AllyBefore = true,
            Team = "Blue",
            Other = "Red",
            LevelsBehind = 3,
            KilledByOther = true,
            StarterMost = 5,
            CapturerMost = 2,
            StarterGroupSeconds = 40,
            StarterHalf = true,
            PitDeaths = 4,
            TogetherSeconds = 30,
        };

    private static string KindWord(ClipTitle.ClipKind kind) =>
        kind switch
        {
            ClipTitle.ClipKind.BStep => "bstep",
            ClipTitle.ClipKind.Triple => "triple kill",
            ClipTitle.ClipKind.Quadra => "quadra kill",
            ClipTitle.ClipKind.Penta => "pentakill",
            _ => "boss",
        };

    /// <summary>Emoji in a title: symbols from U+2600 on, not counting the variation selector.</summary>
    private static int Emoji(string title) =>
        title.EnumerateRunes().Count(rune => rune.Value >= 0x2600 && rune.Value != 0xFE0F);
}
