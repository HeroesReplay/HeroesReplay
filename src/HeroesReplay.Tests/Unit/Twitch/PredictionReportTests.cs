using System.IO;
using System.Linq;
using HeroesReplay.Core.Services.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

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
    public void StreakBook_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-streaks-" + Path.GetRandomFileName());
        try
        {
            var book = new PredictionStreakBook();
            book.Apply("p", "user", true);
            book.Remember("p");
            book.Save(path);
            PredictionStreakBook loaded = PredictionStreakBook.Load(path);
            Assert.Equal(1, loaded.Streaks["user"]);
            Assert.Equal("p", loaded.LastPredictionId);
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
