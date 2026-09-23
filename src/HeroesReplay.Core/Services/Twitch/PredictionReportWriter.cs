using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Helix.Models.Predictions;

namespace HeroesReplay.Core.Services.Twitch;

public sealed class PredictionReportWriter
{
    public const string ReportFileName = "prediction-report.html";
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

    public void TryWriteCurrent(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(ReportPath))
        {
            return;
        }

        try
        {
            var report = new PredictionReport { Title = title };
            File.WriteAllText(ReportPath, PredictionReportPage.ToHtml(report));
            logger.LogInformation("Prediction scene {Path} set to {Title}.", ReportPath, title);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write the current prediction scene.");
        }
    }

    public void TryWrite(Prediction prediction)
    {
        if (prediction == null || string.IsNullOrWhiteSpace(ReportPath))
        {
            return;
        }

        try
        {
            string streakPath = Path.Combine(settings.Location.DataDirectory, StreakFileName);
            PredictionStreakBook streaks = PredictionStreakBook.Load(streakPath);
            PredictionReport report = PredictionReportBuilder.FromPrediction(prediction, streaks);
            if (report == null)
            {
                return;
            }

            streaks.Save(streakPath);
            File.WriteAllText(ReportPath, PredictionReportPage.ToHtml(report));
            logger.LogInformation(
                "Prediction report {Path}: {Winners} winners, {Losers} losers (top predictors only).",
                ReportPath,
                report.Winners.Count,
                report.Losers.Count
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write the prediction report.");
        }
    }
}
