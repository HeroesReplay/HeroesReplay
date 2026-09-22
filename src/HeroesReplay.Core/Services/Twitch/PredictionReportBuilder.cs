using System;
using System.Collections.Generic;
using System.Linq;
using TwitchLib.Api.Helix.Models.Predictions;

namespace HeroesReplay.Core.Services.Twitch;

public static class PredictionReportBuilder
{
    public static PredictionReport FromPrediction(
        Prediction prediction,
        PredictionStreakBook streaks
    )
    {
        if (prediction == null)
        {
            return null;
        }

        string winningId = prediction.WinningOutcomeId;
        var rows = new List<PredictorRow>();
        string winningTitle = null;
        Outcome[] outcomes = prediction.Outcomes ?? Array.Empty<Outcome>();
        foreach (Outcome outcome in outcomes)
        {
            if (outcome == null)
            {
                continue;
            }

            bool won = string.Equals(outcome.Id, winningId, StringComparison.Ordinal);
            if (won)
            {
                winningTitle = outcome.Title;
            }

            TopPredictor[] predictors = outcome.TopPredictors ?? Array.Empty<TopPredictor>();
            foreach (TopPredictor predictor in predictors)
            {
                if (predictor == null || string.IsNullOrWhiteSpace(predictor.UserId))
                {
                    continue;
                }

                rows.Add(
                    new PredictorRow(
                        predictor.UserId,
                        string.IsNullOrWhiteSpace(predictor.UserName)
                            ? predictor.UserLogin
                            : predictor.UserName,
                        predictor.UserLogin,
                        predictor.ChannelPointsUsed,
                        predictor.ChannelPointsWon,
                        won
                    )
                );
            }
        }

        return FromRows(prediction.Id, prediction.Title, winningTitle, rows, streaks);
    }

    public static PredictionReport FromRows(
        string predictionId,
        string title,
        string winningOutcome,
        IReadOnlyList<PredictorRow> rows,
        PredictionStreakBook streaks
    )
    {
        var participants = new List<PredictionParticipant>();
        if (rows != null)
        {
            foreach (PredictorRow row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.UserId))
                {
                    continue;
                }

                int streak = streaks == null ? 0 : streaks.Apply(predictionId, row.UserId, row.Won);
                participants.Add(
                    new PredictionParticipant(
                        row.UserId,
                        row.DisplayName,
                        row.Login,
                        row.PointsUsed,
                        row.PointsWon,
                        row.Won,
                        streak
                    )
                );
            }
        }

        streaks?.Remember(predictionId);
        return new PredictionReport
        {
            PredictionId = predictionId,
            Title = title,
            WinningOutcome = winningOutcome,
            Winners = participants
                .Where(row => row.Won)
                .OrderByDescending(row => row.PointsWon)
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Losers = participants
                .Where(row => !row.Won)
                .OrderByDescending(row => row.PointsUsed)
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }
}

public sealed record PredictorRow(
    string UserId,
    string DisplayName,
    string Login,
    int PointsUsed,
    int PointsWon,
    bool Won
);
