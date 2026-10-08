using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;
using TwitchLib.Api.Helix.Models.Predictions;

namespace HeroesReplay.Core.Twitch.Predictions;

public static class PredictionReportBuilder
{
    /// <summary>
    /// The report for a resolved prediction, with its <see cref="PredictionReport.Verdict"/>.
    /// <paramref name="map"/> is the ledger's map; only a catalog map is named in the verdict.
    /// </summary>
    public static PredictionReport FromPrediction(
        Prediction prediction,
        PredictionStreakBook streaks,
        string map,
        Random random
    )
    {
        if (prediction == null)
        {
            return null;
        }

        string winningId = prediction.WinningOutcomeId;
        var rows = new List<PredictorRow>();
        Outcome winning = null;
        Outcome losing = null;
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
                winning ??= outcome;
            }
            else
            {
                losing ??= outcome;
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

        string canonical = EnglishMapNames.Canonical(map);
        PredictionReport report = FromRows(
            prediction.Id,
            prediction.Title,
            winning?.Title,
            rows,
            streaks
        ) with
        {
            LosingOutcome = winning == null ? null : losing?.Title,
            Map = EnglishMapNames.IsCatalog(canonical) ? canonical : null,
            WinnerVoters = Voters(winning),
            LoserVoters = Voters(losing),
            WinnerPoints = Points(winning),
            LoserPoints = Points(losing),
        };
        return WithVerdict(report, streaks, random);
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

    /// <summary>
    /// Adds a <see cref="PredictionVerdict"/> line and remembers its templates in
    /// <paramref name="streaks"/>, so the next prediction does not open with the same words.
    /// </summary>
    public static PredictionReport WithVerdict(
        PredictionReport report,
        PredictionStreakBook streaks,
        Random random
    )
    {
        PredictionVerdictLine line = PredictionVerdict.Compose(
            report,
            streaks?.RecentVerdicts,
            random
        );
        if (line == null)
        {
            return report;
        }

        streaks?.RememberVerdict(line.Templates);
        return report with { Verdict = line.Text };
    }

    // TwitchLib names these the wrong way round: Outcome.ChannelPoints is Helix `users` and
    // Outcome.ChannelPointsVotes is Helix `channel_points`.
    private static int Voters(Outcome outcome) => outcome == null ? 0 : outcome.ChannelPoints;

    private static int Points(Outcome outcome) => outcome == null ? 0 : outcome.ChannelPointsVotes;
}

public sealed record PredictorRow(
    string UserId,
    string DisplayName,
    string Login,
    int PointsUsed,
    int PointsWon,
    bool Won
);
