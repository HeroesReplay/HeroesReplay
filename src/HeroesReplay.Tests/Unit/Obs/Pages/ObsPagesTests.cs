using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs.Pages;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs.Pages;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsPagesTests
{
    [Fact]
    public void Write_RendersTheQueuePageWithThisBuild()
    {
        string data = NewDataDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(data, "requests.json"),
                "[{\"Request\":{\"Login\":\"kazpa\"}}]"
            );
            File.WriteAllText(
                Path.Combine(data, QueueBoard.FileName),
                "<div class=\"team blue\"><i>Blue</i><b>1</b><b>2</b><b>3</b><b>4</b><b>5</b></div>"
            );

            ObsPageResult queue = Page(ObsPages.Write(Settings(data)), QueueBoard.FileName);

            Assert.Equal(ObsPageOutcome.Written, queue.Outcome);
            Assert.Equal("1 request waiting.", queue.Detail);
            string html = File.ReadAllText(Path.Combine(data, QueueBoard.FileName));
            Assert.Contains("1 request waiting", html);
            Assert.Contains("kazpa", html);
            Assert.Contains(
                "<div class=\"team blue\"><i>Blue</i><b>5</b><b>4</b><b>3</b><b>2</b><b>1</b></div>",
                html
            );
            // The reward names come from Maps:Catalog without loading heroes-data2.
            Assert.Contains("<span class=\"label\">Cursed Hollow (SL)</span>", html);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Write_NoQueueFile_RendersAnEmptyQueue()
    {
        string data = NewDataDirectory();
        try
        {
            ObsPageResult queue = Page(ObsPages.Write(Settings(data)), QueueBoard.FileName);

            Assert.Equal(ObsPageOutcome.Written, queue.Outcome);
            Assert.Contains(
                "The queue is empty.",
                File.ReadAllText(Path.Combine(data, QueueBoard.FileName))
            );
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Write_UnreadableQueue_FailsAndLeavesThePageAndTheQueueFile()
    {
        string data = NewDataDirectory();
        try
        {
            string queueFile = Path.Combine(data, "requests.json");
            File.WriteAllText(queueFile, "not json");
            File.WriteAllText(Path.Combine(data, QueueBoard.FileName), "stale");

            ObsPageResult queue = Page(ObsPages.Write(Settings(data)), QueueBoard.FileName);

            Assert.Equal(ObsPageOutcome.Failed, queue.Outcome);
            Assert.Equal("stale", File.ReadAllText(Path.Combine(data, QueueBoard.FileName)));
            Assert.Equal("not json", File.ReadAllText(queueFile));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Write_NoSavedPrediction_KeepsThePredictionPage()
    {
        string data = NewDataDirectory();
        try
        {
            string page = Path.Combine(data, PredictionReportWriter.ReportFileName);
            File.WriteAllText(page, "stale");

            ObsPageResult prediction = Page(
                ObsPages.Write(Settings(data)),
                PredictionReportWriter.ReportFileName
            );

            Assert.Equal(ObsPageOutcome.Kept, prediction.Outcome);
            Assert.Equal("stale", File.ReadAllText(page));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Write_SavedPrediction_RendersThePredictionPageAgain()
    {
        string data = NewDataDirectory();
        try
        {
            AppSettings settings = Settings(data);
            new PredictionReportWriter(
                NullLogger<PredictionReportWriter>.Instance,
                settings
            ).TryWriteCurrent("Sky Temple: who wins?");
            string page = Path.Combine(data, PredictionReportWriter.ReportFileName);
            Assert.True(
                File.Exists(Path.Combine(data, PredictionReportWriter.SavedReportFileName))
            );
            File.WriteAllText(page, "stale");

            ObsPageResult prediction = Page(
                ObsPages.Write(settings),
                PredictionReportWriter.ReportFileName
            );

            Assert.Equal(ObsPageOutcome.Written, prediction.Outcome);
            Assert.Contains("<h1>Sky Temple: who wins?</h1>", File.ReadAllText(page));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Write_UnreadableSavedPrediction_FailsAndLeavesThePage()
    {
        string data = NewDataDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(data, PredictionReportWriter.SavedReportFileName),
                "not json"
            );
            string page = Path.Combine(data, PredictionReportWriter.ReportFileName);
            File.WriteAllText(page, "stale");

            ObsPageResult prediction = Page(
                ObsPages.Write(Settings(data)),
                PredictionReportWriter.ReportFileName
            );

            Assert.Equal(ObsPageOutcome.Failed, prediction.Outcome);
            Assert.Equal("stale", File.ReadAllText(page));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Reload_ReloadsOnlyTheBrowserSourcesThatShowAWrittenPage()
    {
        string data = NewDataDirectory();
        try
        {
            FakeObs obs = FakeObs.Installed(data);
            using IObsPageSession session = obs.OpenPage();

            IReadOnlyList<string> reloaded = ObsPages.Reload(
                session,
                data,
                new[] { QueueBoard.FileName, PredictionReportWriter.ReportFileName }
            );

            Assert.Equal(
                new[] { "prediction-report-browser", "request-queue-browser" },
                reloaded.Order()
            );
            Assert.Equal(reloaded, obs.Reloaded);
            // The match report (a web page) and the countdown (a packaged asset) are not reloaded.
            Assert.DoesNotContain("match-report-browser", obs.Reloaded);
            Assert.DoesNotContain("countdown", obs.Reloaded);
            Assert.All(
                obs.Requests,
                request =>
                    Assert.True(
                        request.StartsWith("Get", System.StringComparison.Ordinal)
                            || request == "PressInputPropertiesButton",
                        request
                    )
            );
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Reload_OnlyThePagesThatWereWritten()
    {
        string data = NewDataDirectory();
        try
        {
            FakeObs obs = FakeObs.Installed(data);
            using IObsPageSession session = obs.OpenPage();

            ObsPages.Reload(session, data, new[] { QueueBoard.FileName });

            Assert.Equal(new[] { "request-queue-browser" }, obs.Reloaded);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Reload_MatchesTheLocalFileSettingOfABrowserSource()
    {
        string data = NewDataDirectory();
        try
        {
            FakeObs obs = FakeObs.Installed(data);
            var settings = (Newtonsoft.Json.Linq.JObject)
                obs.Source("request-queue-browser")["settings"];
            settings["is_local_file"] = true;
            settings["local_file"] = Path.Combine(data, QueueBoard.FileName).Replace('\\', '/');
            settings["url"] = "";
            using IObsPageSession session = obs.OpenPage();

            ObsPages.Reload(session, data, new[] { QueueBoard.FileName });

            Assert.Equal(new[] { "request-queue-browser" }, obs.Reloaded);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Reload_NothingWritten_SendsNothing()
    {
        FakeObs obs = FakeObs.Installed(Path.GetTempPath());
        using IObsPageSession session = obs.OpenPage();

        Assert.Empty(ObsPages.Reload(session, Path.GetTempPath(), new string[0]));
        Assert.Empty(obs.Requests);
    }

    private static ObsPageResult Page(IReadOnlyList<ObsPageResult> results, string fileName) =>
        results.Single(result => result.FileName == fileName);

    private static string NewDataDirectory()
    {
        string data = Path.Combine(Path.GetTempPath(), "hr-obs-pages-" + Path.GetRandomFileName());
        Directory.CreateDirectory(data);
        return data;
    }

    private static AppSettings Settings(string data) =>
        new()
        {
            Location = new LocationSettings { DataDirectory = data },
            Twitch = new TwitchSettings { QueueFileName = "requests.json" },
            Maps = new MapSettings
            {
                Catalog = new[]
                {
                    new MapDefinition
                    {
                        Name = "Cursed Hollow",
                        ShortName = "CursedHollow",
                        Type = "standard",
                        RankedRotation = true,
                        Playable = true,
                    },
                },
            },
        };
}
