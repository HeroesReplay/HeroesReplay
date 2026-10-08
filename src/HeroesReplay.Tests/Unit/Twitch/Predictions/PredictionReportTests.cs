using System.IO;
using System.Linq;
using HeroesReplay.Core.Twitch.Predictions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PredictionReportTests
{
    [Fact]
    public void Build_SortsWinnersAndResetsLoserStreaks()
    {
        var streaks = new PredictionStreakBook();
        streaks.Apply("older", "loser", true);
        streaks.Remember("older");

        PredictionReport report = PredictionReportBuilder.FromRows(
            "pred-1",
            "Cursed Hollow",
            "Blue",
            new[]
            {
                new PredictorRow("winner", "Winner", "winner", 100, 250, true),
                new PredictorRow("loser", "Loser", "loser", 80, 0, false),
            },
            streaks
        );

        Assert.Equal("Winner", report.Winners.Single().DisplayName);
        Assert.Equal(1, report.Winners.Single().Streak);
        Assert.Equal(250, report.Winners.Single().PointsWon);
        Assert.Equal(0, report.Losers.Single().Streak);
        Assert.Equal(1, streaks.Apply("pred-1", "winner", true));
    }

    [Fact]
    public void Page_NamesWinnersAndSaysTopPredictorsOnly()
    {
        string html = PredictionReportPage.ToHtml(
            new PredictionReport
            {
                Title = "Cursed Hollow <map>",
                WinningOutcome = "Blue",
                Winners = new[] { new PredictionParticipant("1", "Ana", "ana", 10, 40, true, 3) },
                Losers = new PredictionParticipant[0],
            }
        );

        Assert.Contains("Ana", html);
        Assert.Contains("top predictors", html);
        Assert.Contains("Cursed Hollow &lt;map&gt;", html);
        Assert.DoesNotContain("<map>", html);
    }

    [Fact]
    public void Page_ShowsTheVerdictAndBothSides()
    {
        string html = PredictionReportPage.ToHtml(
            new PredictionReport
            {
                Title = "Cursed Hollow: who wins?",
                WinningOutcome = "Blue",
                LosingOutcome = "Red",
                WinnerVoters = 1,
                LoserVoters = 12,
                WinnerPoints = 500,
                LoserPoints = 12000,
                Verdict = "Blue defies the chat and wins. <Delicious>.",
            }
        );

        Assert.Contains(
            "<p class=\"verdict\">Blue defies the chat and wins. &lt;Delicious&gt;.</p>",
            html
        );
        Assert.Contains("Blue: 1 viewer, 500 points.", html);
        Assert.Contains("Red: 12 viewers, 12,000 points.", html);
        Assert.Contains("top predictors", html);
    }

    [Fact]
    public void Page_FromAReportSavedBeforeVerdicts_StillSaysWhoWon()
    {
        string html = PredictionReportPage.ToHtml(
            new PredictionReport { Title = "Cursed Hollow: who wins?", WinningOutcome = "Red" }
        );

        Assert.DoesNotContain("class=\"verdict\"", html);
        Assert.Contains("Red won.", html);
        Assert.DoesNotContain("viewers", html);
    }

    [Fact]
    public void StreakBook_RememberVerdict_KeepsTheNewestFew()
    {
        var book = new PredictionStreakBook();

        for (int i = 0; i < 10; i++)
        {
            book.RememberVerdict(new[] { "opening " + i, "callout " + i });
        }

        book.RememberVerdict(new[] { "opening 7" });

        Assert.Equal(PredictionVerdict.RecentToAvoid, book.RecentVerdicts.Count);
        Assert.Equal("opening 7", book.RecentVerdicts[0]);
        Assert.Equal("callout 9", book.RecentVerdicts[1]);
        Assert.Single(book.RecentVerdicts, entry => entry == "opening 7");
    }

    [Fact]
    public void StreakBook_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-streaks-" + Path.GetRandomFileName());
        try
        {
            var book = new PredictionStreakBook();
            book.Apply("p", "user", true);
            book.Remember("p");
            book.RememberVerdict(new[] { "{winner} wins." });
            book.Save(path);
            PredictionStreakBook loaded = PredictionStreakBook.Load(path);
            Assert.Equal(1, loaded.Streaks["user"]);
            Assert.Equal("p", loaded.LastPredictionId);
            Assert.Equal(new[] { "{winner} wins." }, loaded.RecentVerdicts);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
