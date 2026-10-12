using System;
using System.Collections.Generic;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;

namespace HeroesReplay.Core.Twitch.Predictions;

/// <summary>Which question a channel prediction asks.</summary>
public enum PredictionKind
{
    /// <summary>Blue or Red: who wins the match.</summary>
    Winner,

    /// <summary>The rating of a Blind Rating match, Bronze to Master (#411).</summary>
    Rating,
}

/// <summary>
/// The Blind Rating prediction (#411): viewers guess the rank of a match whose rank stays off
/// the stream. It replaces Blue/Red for that replay, because Twitch runs one prediction per
/// channel at a time. Grandmaster counts as Master: the issue's six outcomes.
/// </summary>
public static class RatingPrediction
{
    public const string Title = "Guess the rating of the match";

    public static readonly IReadOnlyList<string> Outcomes = new[]
    {
        "Bronze",
        "Silver",
        "Gold",
        "Platinum",
        "Diamond",
        "Master",
    };

    /// <summary>
    /// The rating outcome of <paramref name="replay"/>, from the rank the game scene would show
    /// (<see cref="RankImage.RankOf"/>). Null when the replay has no known rank.
    /// </summary>
    public static string OutcomeFor(LoadedReplay replay)
    {
        string rank = RankImage.RankOf(replay, out int? leagueTier);
        string key = RankImage.Normalize(rank, leagueTier);
        if (key == null)
        {
            return null;
        }

        if (key == "grandmaster")
        {
            return "Master";
        }

        foreach (string outcome in Outcomes)
        {
            if (string.Equals(outcome, key, StringComparison.OrdinalIgnoreCase))
            {
                return outcome;
            }
        }

        return null;
    }

    /// <summary>A Blind Rating replay with a known rank gets the rating prediction.</summary>
    public static bool Applies(LoadedReplay replay) =>
        ReplayRequestKind.Blind(replay) && OutcomeFor(replay) != null;

    /// <summary>The outcome's position in <see cref="Outcomes"/>, or null.</summary>
    public static int? OutcomeIndex(string outcome)
    {
        for (int index = 0; index < Outcomes.Count; index++)
        {
            if (string.Equals(Outcomes[index], outcome?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>The outcome titles a prediction of <paramref name="kind"/> offers, in order.</summary>
    public static IReadOnlyList<string> OutcomesFor(PredictionKind kind) =>
        kind == PredictionKind.Rating
            ? Outcomes
            : new[] { MatchPrediction.Blue, MatchPrediction.Red };

    /// <summary>The title of outcome <paramref name="index"/>, or null when out of range.</summary>
    public static string OutcomeTitle(PredictionKind kind, int? index)
    {
        IReadOnlyList<string> outcomes = OutcomesFor(kind);
        return index is int value && value >= 0 && value < outcomes.Count ? outcomes[value] : null;
    }
}
