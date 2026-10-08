using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Twitch.Predictions;
using Newtonsoft.Json;
using TwitchLib.Api.Helix.Models.Predictions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PredictionVerdictTests
{
    private const string LongName = "Abcdefghijklmnopqrstuvwxy";

    [Theory]
    [InlineData(0, 0, 0, 0, PredictionVerdictKind.Silence)]
    [InlineData(5, 0, 1000, 0, PredictionVerdictKind.Unanimous)]
    [InlineData(0, 4, 0, 900, PredictionVerdictKind.Wipeout)]
    [InlineData(3, 10, 500, 9000, PredictionVerdictKind.Upset)]
    [InlineData(10, 3, 9000, 500, PredictionVerdictKind.Favourite)]
    [InlineData(4, 4, 1000, 1000, PredictionVerdictKind.EvenMatch)]
    [InlineData(2, 6, 0, 0, PredictionVerdictKind.Upset)]
    [InlineData(9, 1, 100, 5000, PredictionVerdictKind.Upset)]
    public void Kind_ReadsHowTheViewersDid(
        int winnerVoters,
        int loserVoters,
        int winnerPoints,
        int loserPoints,
        PredictionVerdictKind expected
    )
    {
        PredictionReport report = Report(winnerVoters, loserVoters, winnerPoints, loserPoints);

        Assert.Equal(expected, PredictionVerdict.Kind(report));
    }

    [Fact]
    public void Kind_CountsTopPredictorsWhenHelixSentNoTotals()
    {
        PredictionReport report = Report(0, 0, 0, 0) with
        {
            Winners = new[] { Winner("Ana", 400, 1) },
        };

        Assert.Equal(PredictionVerdictKind.Unanimous, PredictionVerdict.Kind(report));
    }

    [Fact]
    public void Compose_WithoutAWinner_IsNull()
    {
        Assert.Null(PredictionVerdict.Compose(new PredictionReport(), null, new Random(1)));
        Assert.Null(PredictionVerdict.Compose(null, null, new Random(1)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 0, 1000, 0)]
    [InlineData(0, 4, 0, 900)]
    [InlineData(3, 10, 500, 9000)]
    [InlineData(10, 3, 9000, 500)]
    [InlineData(4, 4, 1000, 1000)]
    public void Compose_FillsEveryPlaceholderAndFitsChat(
        int winnerVoters,
        int loserVoters,
        int winnerPoints,
        int loserPoints
    )
    {
        PredictionReport report = Report(winnerVoters, loserVoters, winnerPoints, loserPoints);
        if (winnerVoters > 0)
        {
            report = report with { Winners = new[] { Winner("Ana", 4000, 1) } };
        }

        if (loserVoters > 0)
        {
            report = report with { Losers = new[] { Loser("Bo", 900) } };
        }

        for (int seed = 0; seed < 200; seed++)
        {
            PredictionVerdictLine line = PredictionVerdict.Compose(report, null, new Random(seed));

            Assert.DoesNotContain("{", line.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("}", line.Text, StringComparison.Ordinal);
            Assert.True(line.Text.Length <= PredictionVerdict.ChatLimit, line.Text);
            Assert.Contains(
                PredictionVerdict.OpeningsFor(PredictionVerdict.Kind(report)),
                template => template == line.Templates[0]
            );
        }
    }

    [Fact]
    public void Compose_WithAWinnerAndALoser_AddsACallout()
    {
        PredictionReport report = Report(3, 10, 500, 9000) with
        {
            Winners = new[] { Winner("Ana", 4000, 1) },
            Losers = new[] { Loser("Bo", 900) },
        };

        PredictionVerdictLine line = PredictionVerdict.Compose(report, null, new Random(7));

        Assert.Equal(2, line.Templates.Count);
        Assert.Contains(
            PredictionVerdict.TopCallouts.Concat(PredictionVerdict.LoserCallouts),
            template => template == line.Templates[1]
        );
        Assert.True(
            line.Text.Contains("Ana", StringComparison.Ordinal)
                || line.Text.Contains("Bo", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Compose_NamesAStreakWhenTheTopWinnerIsOnARoll()
    {
        PredictionReport report = Report(3, 10, 500, 9000) with
        {
            Winners = new[] { Winner("Ana", 12500, 4) },
            Losers = new[] { Loser("Bo", 900) },
        };

        for (int seed = 0; seed < 20; seed++)
        {
            PredictionVerdictLine line = PredictionVerdict.Compose(report, null, new Random(seed));

            Assert.Contains(
                PredictionVerdict.StreakCallouts,
                template => template == line.Templates[1]
            );
            Assert.Contains("Ana", line.Text, StringComparison.Ordinal);
            Assert.Contains("4", line.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Compose_FormatsPointsWithSeparators()
    {
        PredictionReport report = Report(1, 0, 12500, 0) with
        {
            Winners = new[] { Winner("Ana", 12500, 1) },
        };

        PredictionVerdictLine line = PredictionVerdict.Compose(report, null, new Random(3));

        Assert.Contains("12,500", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NamesTheMapOnlyWhenItIsKnown()
    {
        PredictionReport withoutMap = Report(3, 10, 500, 9000);
        PredictionReport withMap = withoutMap with { Map = "Cursed Hollow" };
        var mapLines = new HashSet<string>();

        for (int seed = 0; seed < 200; seed++)
        {
            PredictionVerdictLine blind = PredictionVerdict.Compose(
                withoutMap,
                null,
                new Random(seed)
            );
            PredictionVerdictLine named = PredictionVerdict.Compose(
                withMap,
                null,
                new Random(seed)
            );

            Assert.DoesNotContain("{map}", blind.Templates[0], StringComparison.Ordinal);
            if (named.Templates[0].Contains("{map}", StringComparison.Ordinal))
            {
                Assert.Contains("Cursed Hollow", named.Text, StringComparison.Ordinal);
                mapLines.Add(named.Templates[0]);
            }
        }

        Assert.NotEmpty(mapLines);
    }

    [Fact]
    public void Compose_SkipsRecentTemplatesWhileAnotherFits()
    {
        string[] silence = PredictionVerdict.Openings[PredictionVerdictKind.Silence];
        string[] recent = silence.Skip(1).ToArray();

        for (int seed = 0; seed < 20; seed++)
        {
            PredictionVerdictLine line = PredictionVerdict.Compose(
                Report(0, 0, 0, 0),
                recent,
                new Random(seed)
            );

            Assert.Equal(silence[0], line.Templates[0]);
        }
    }

    [Fact]
    public void Compose_WhenEveryTemplateIsRecent_StillSpeaks()
    {
        string[] silence = PredictionVerdict.Openings[PredictionVerdictKind.Silence];

        PredictionVerdictLine line = PredictionVerdict.Compose(
            Report(0, 0, 0, 0),
            silence,
            new Random(5)
        );

        Assert.Contains(silence, template => template == line.Templates[0]);
    }

    [Fact]
    public void EveryTemplate_FitsChatWithTheLongestValues()
    {
        var values = new Dictionary<string, string>
        {
            ["winner"] = "Blue",
            ["loser"] = "Red",
            ["map"] = "Tomb of the Spider Queen",
            ["winners"] = "2,147,483,647",
            ["losers"] = "2,147,483,647",
            ["pool"] = "2,147,483,647",
            ["top"] = LongName,
            ["won"] = "2,147,483,647",
            ["streak"] = "2,147,483,647",
            ["victim"] = LongName,
            ["spent"] = "2,147,483,647",
        };
        string[] openings = PredictionVerdict
            .Openings.Values.SelectMany(lines => lines)
            .Concat(PredictionVerdict.Contest)
            .ToArray();
        string[] callouts = PredictionVerdict
            .TopCallouts.Concat(PredictionVerdict.StreakCallouts)
            .Concat(PredictionVerdict.LoserCallouts)
            .ToArray();

        int longestOpening = openings.Max(template =>
            PredictionVerdict.Fill(template, values).Length
        );
        int longestCallout = callouts.Max(template =>
            PredictionVerdict.Fill(template, values).Length
        );

        Assert.All(
            openings.Concat(callouts),
            template => Assert.True(PredictionVerdict.Fits(template, values), template)
        );
        Assert.True(
            longestOpening + 1 + longestCallout <= PredictionVerdict.ChatLimit,
            $"{longestOpening} + {longestCallout}"
        );
    }

    [Fact]
    public void EveryTemplate_IsUnique()
    {
        string[] all = PredictionVerdict
            .Openings.Values.SelectMany(lines => lines)
            .Concat(PredictionVerdict.Contest)
            .Concat(PredictionVerdict.TopCallouts)
            .Concat(PredictionVerdict.StreakCallouts)
            .Concat(PredictionVerdict.LoserCallouts)
            .ToArray();

        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.True(all.Length >= 50, all.Length.ToString());
    }

    [Fact]
    public void WithVerdict_DoesNotOpenWithTheSameLineTwiceInARow()
    {
        var streaks = new PredictionStreakBook();
        var random = new Random(11);
        PredictionReport report = Report(3, 10, 500, 9000) with
        {
            Map = "Dragon Shire",
            Winners = new[] { Winner("Ana", 4000, 1) },
            Losers = new[] { Loser("Bo", 900) },
        };
        IReadOnlyDictionary<string, string> values = PredictionVerdict.Values(report);
        string[] openings = PredictionVerdict
            .OpeningsFor(PredictionVerdictKind.Upset)
            .Where(template => PredictionVerdict.Fits(template, values))
            .ToArray();
        string previous = null;

        for (int round = 0; round < 40; round++)
        {
            PredictionReport spoken = PredictionReportBuilder.WithVerdict(report, streaks, random);
            string opening = openings.Single(template =>
                spoken.Verdict.StartsWith(
                    PredictionVerdict.Fill(template, values),
                    StringComparison.Ordinal
                )
            );

            Assert.NotEqual(previous, opening);
            Assert.True(streaks.RecentVerdicts.Count <= PredictionVerdict.RecentToAvoid);
            previous = opening;
        }
    }

    [Fact]
    public void FromPrediction_ReadsHelixTotalsAndNamesTheMap()
    {
        Prediction prediction = Helix(
            """
            {
              "id": "pred-9",
              "title": "Cursed Hollow: who wins?",
              "winning_outcome_id": "blue",
              "status": "RESOLVED",
              "outcomes": [
                {
                  "id": "blue", "title": "Blue", "users": 2, "channel_points": 1500,
                  "top_predictors": [
                    { "user_id": "1", "user_name": "Ana", "user_login": "ana",
                      "channel_points_used": 1000, "channel_points_won": 4000 },
                    { "user_id": "2", "user_name": "Bo", "user_login": "bo",
                      "channel_points_used": 500, "channel_points_won": 2000 }
                  ]
                },
                {
                  "id": "red", "title": "Red", "users": 5, "channel_points": 4500,
                  "top_predictors": [
                    { "user_id": "3", "user_name": "Cy", "user_login": "cy",
                      "channel_points_used": 3000, "channel_points_won": 0 }
                  ]
                }
              ]
            }
            """
        );
        var streaks = new PredictionStreakBook();

        PredictionReport report = PredictionReportBuilder.FromPrediction(
            prediction,
            streaks,
            "cursedhollow",
            new Random(2)
        );

        Assert.Equal("Blue", report.WinningOutcome);
        Assert.Equal("Red", report.LosingOutcome);
        Assert.Equal("Cursed Hollow", report.Map);
        Assert.Equal(2, report.WinnerVoters);
        Assert.Equal(5, report.LoserVoters);
        Assert.Equal(1500, report.WinnerPoints);
        Assert.Equal(4500, report.LoserPoints);
        Assert.Equal(PredictionVerdictKind.Upset, PredictionVerdict.Kind(report));
        Assert.False(string.IsNullOrWhiteSpace(report.Verdict));
        Assert.Equal(2, streaks.RecentVerdicts.Count);
        Assert.Equal("Ana", report.Winners[0].DisplayName);
    }

    [Fact]
    public void FromPrediction_DropsAMapThatIsNotInTheCatalog()
    {
        Prediction prediction = Helix(
            """
            {
              "id": "pred-10", "title": "Who wins?", "winning_outcome_id": "red",
              "status": "RESOLVED",
              "outcomes": [ { "id": "blue", "title": "Blue" }, { "id": "red", "title": "Red" } ]
            }
            """
        );

        PredictionReport report = PredictionReportBuilder.FromPrediction(
            prediction,
            new PredictionStreakBook(),
            "Test",
            new Random(4)
        );

        Assert.Null(report.Map);
        Assert.Equal("Red", report.WinningOutcome);
        Assert.Equal("Blue", report.LosingOutcome);
        Assert.Equal(PredictionVerdictKind.Silence, PredictionVerdict.Kind(report));
        Assert.StartsWith("Red", report.Verdict, StringComparison.Ordinal);
    }

    private static Prediction Helix(string json) => JsonConvert.DeserializeObject<Prediction>(json);

    private static PredictionReport Report(
        int winnerVoters,
        int loserVoters,
        int winnerPoints,
        int loserPoints
    ) =>
        new()
        {
            PredictionId = "pred",
            Title = "Who wins?",
            WinningOutcome = "Blue",
            LosingOutcome = "Red",
            WinnerVoters = winnerVoters,
            LoserVoters = loserVoters,
            WinnerPoints = winnerPoints,
            LoserPoints = loserPoints,
        };

    private static PredictionParticipant Winner(string name, int won, int streak) =>
        new(name.ToLowerInvariant(), name, name.ToLowerInvariant(), won / 4, won, true, streak);

    private static PredictionParticipant Loser(string name, int spent) =>
        new(name.ToLowerInvariant(), name, name.ToLowerInvariant(), spent, 0, false, 0);
}
