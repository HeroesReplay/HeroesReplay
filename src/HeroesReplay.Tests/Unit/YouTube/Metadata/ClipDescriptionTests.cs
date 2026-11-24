using System;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.BSteps;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>
/// A clip's description for viewers: what happens, told warmly, with no seconds, counts or
/// reasons the clip was picked. The same clip always gets the same words.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClipDescriptionTests
{
    [Fact]
    public void ABossSteal_TellsTheStory_WithoutTimingsOrHeadcounts()
    {
        TeamKillClip clip = Assert.Single(BossFightClips.Select(Insights(Steal())));

        for (int id = 66698600; id < 66698620; id++)
        {
            string text = string.Join(
                " ",
                ClipDescription.Compose(clip, "Cursed Hollow", id.ToString())
            );

            Assert.Contains("Illidan", text, StringComparison.Ordinal);
            Assert.DoesNotContain(text, char.IsDigit);
            Assert.DoesNotContain("heroes on it", text, StringComparison.Ordinal);
            Assert.Equal(
                text,
                string.Join(" ", ClipDescription.Compose(clip, "Cursed Hollow", id.ToString()))
            );
        }
    }

    [Fact]
    public void ABossTakenFromTheTeamThatKilledIt_NamesBothTeams()
    {
        BossFight fight = Steal() with { KillerTeam = 0, KillerHero = "Abathur" };
        TeamKillClip clip = Assert.Single(BossFightClips.Select(Insights(fight)));

        string text = string.Join(" ", ClipDescription.Compose(clip, "Cursed Hollow", "66698600"));

        Assert.Contains("Blue", text, StringComparison.Ordinal);
        Assert.Contains("Red", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Red team", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABstep_NeverCountsTheCancels_NorExplainsWhatABstepIs()
    {
        TeamKillClip clip = BStepClips.Clip(
            new BStep
            {
                PlayerIndex = 0,
                Team = 0,
                Hero = "Sonya",
                StartSecond = 300,
                EndSecond = 304,
                Cycles = 9,
                Deaths = new[]
                {
                    new BStepDeath(5, "Muradin", 298, BStepDeathKind.KilledByBstepper, false, 0),
                },
            }
        );

        var lines = ClipDescription.Compose(clip, "Cursed Hollow", "66698600");

        Assert.Contains("Muradin", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(lines[0], char.IsDigit);
        Assert.DoesNotContain("hearth cancels", string.Join(" ", lines), StringComparison.Ordinal);
        Assert.Single(lines);
        Assert.DoesNotContain("Hearthstone", lines[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 3, "had killed Sonya three times", "after dying to Muradin three times")]
    [InlineData(2, 0, "third time Sonya has taken down Muradin", "third kill on them")]
    [InlineData(3, 2, "traded kills all game", "traded kills all game")]
    public void ABstep_TellsHowItCameAbout_RevengeOrDomination(
        int killsOnRival,
        int deathsToRival,
        string one,
        string other
    )
    {
        TeamKillClip clip = BStepClips.Clip(Rivalry(killsOnRival, deathsToRival));

        for (int id = 66698600; id < 66698610; id++)
        {
            string text = Assert.Single(
                ClipDescription.Compose(clip, "Cursed Hollow", id.ToString())
            );

            Assert.True(
                text.Contains(one, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(other, StringComparison.OrdinalIgnoreCase),
                text
            );
            Assert.DoesNotContain(text, char.IsDigit);
        }
    }

    [Fact]
    public void ABstep_AfterOneEarlierMeeting_IsNoRivalry()
    {
        TeamKillClip clip = BStepClips.Clip(Rivalry(1, 1));

        string text = Assert.Single(ClipDescription.Compose(clip, "Cursed Hollow", "66698600"));

        Assert.DoesNotContain("revenge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("all game", text, StringComparison.Ordinal);
        Assert.Contains("Muradin", text, StringComparison.Ordinal);
    }

    private static BStep Rivalry(int killsOnRival, int deathsToRival) =>
        new()
        {
            PlayerIndex = 0,
            Team = 0,
            Hero = "Sonya",
            StartSecond = 300,
            EndSecond = 304,
            Cycles = 9,
            Deaths = new[]
            {
                new BStepDeath(5, "Muradin", 298, BStepDeathKind.KilledByBstepper, false, 0),
            },
            RivalIndex = 5,
            Rival = "Muradin",
            EarlierKillsOnRival = killsOnRival,
            EarlierDeathsToRival = deathsToRival,
        };

    [Fact]
    public void EveryLine_NamesNoNumbers()
    {
        foreach (ClipDescription.Line line in ClipDescription.Lines)
        {
            var facts = new ClipTitle.Facts
            {
                Kind = line.Kind,
                Hero = "Li-Ming",
                Victims = "Jaina and Uther",
                VictimCount = 2,
                DiedAfter = true,
                AllyBefore = true,
                Wiped = true,
                Team = "Blue",
                Other = "Red",
                LevelsBehind = 3,
                KilledByOther = true,
                StarterMost = 5,
                PitDeaths = 4,
                Rival = "Uther",
                KillsOnRival = 3,
                DeathsToRival = 2,
            };
            string text = line.Text(new ClipDescription.Context(facts, false, "Cursed Hollow"));

            Assert.DoesNotContain(text, char.IsDigit);
            Assert.DoesNotContain(" s ", text, StringComparison.Ordinal);
        }
    }

    private static BossFightInsights Insights(BossFight fight) =>
        new() { Fights = new[] { fight } };

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
}
