using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Helix.Models.Predictions;

namespace HeroesReplay.Core.Twitch.Predictions;

public sealed class PredictionReportWriter
{
    public const string ReportFileName = "prediction-report.html";
    public const string SavedReportFileName = "prediction-report.json";
    public const string StreakFileName = "prediction-streaks.json";

    private readonly ILogger<PredictionReportWriter> logger;
    private readonly AppSettings settings;

    public PredictionReportWriter(ILogger<PredictionReportWriter> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string ReportPath =>
        settings.Location == null
            ? null
            : Path.Combine(settings.Location.DataDirectory, ReportFileName);

    /// <summary>
    /// Renders <see cref="ReportFileName"/> again from the report saved last in
    /// <paramref name="dataDirectory"/>, so the page matches this build. False when no report
    /// was saved yet; the page is left as it is.
    /// </summary>
    /// <exception cref="IOException">The saved report or the page could not be read or written.</exception>
    /// <exception cref="JsonException">The saved report is not a prediction report.</exception>
    public static bool TryRewrite(string dataDirectory)
    {
        string saved = Path.Combine(dataDirectory, SavedReportFileName);
        if (!File.Exists(saved))
        {
            return false;
        }

        PredictionReport report =
            JsonSerializer.Deserialize<PredictionReport>(File.ReadAllText(saved))
            ?? throw new JsonException(SavedReportFileName + " is empty.");
        DurableFile.Replace(
            Path.Combine(dataDirectory, ReportFileName),
            PredictionReportPage.ToHtml(report)
        );
        return true;
    }

    public void TryWriteCurrent(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(ReportPath))
        {
            return;
        }

        try
        {
            Write(new PredictionReport { Title = title });
            logger.LogInformation("Prediction scene {Path} set to {Title}.", ReportPath, title);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write the current prediction scene.");
        }
    }

    /// <summary>
    /// Builds the report for a resolved <paramref name="prediction"/>, updates the streaks and
    /// the verdict memory, and writes the page. Returns the report (with its verdict) even when
    /// the page could not be written; null when no report could be built.
    /// </summary>
    public PredictionReport TryWrite(Prediction prediction, string map)
    {
        if (prediction == null || string.IsNullOrWhiteSpace(ReportPath))
        {
            return null;
        }

        PredictionReport report;
        try
        {
            string streakPath = Path.Combine(settings.Location.DataDirectory, StreakFileName);
            PredictionStreakBook streaks = PredictionStreakBook.Load(streakPath);
            report = PredictionReportBuilder.FromPrediction(
                prediction,
                streaks,
                map,
                Random.Shared
            );
            if (report == null)
            {
                return null;
            }

            streaks.Save(streakPath);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not build the prediction report.");
            return null;
        }

        try
        {
            Write(report);
            logger.LogInformation(
                "Prediction report {Path}: {Winners} winners, {Losers} losers (top predictors only). Verdict: {Verdict}",
                ReportPath,
                report.Winners.Count,
                report.Losers.Count,
                report.Verdict
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write the prediction report.");
        }

        return report;
    }

    private void Write(PredictionReport report)
    {
        // The report is saved beside the page so `obs pages` can render it again after an update.
        DurableFile.Replace(
            Path.Combine(settings.Location.DataDirectory, SavedReportFileName),
            JsonSerializer.Serialize(report)
        );
        DurableFile.Replace(ReportPath, PredictionReportPage.ToHtml(report));
    }
}
