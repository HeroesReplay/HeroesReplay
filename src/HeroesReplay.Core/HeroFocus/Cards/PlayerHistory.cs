using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Core.YouTube.Metadata;

namespace HeroesReplay.Core.HeroFocus.Cards;

public enum PlayerHistoryStatus
{
    /// <summary>Heroes Profile answered with the player's games.</summary>
    Known,

    /// <summary>Not looked up yet, or the lookup failed or was out of budget.</summary>
    Unknown,

    /// <summary>The player's profile is private: no story is ever told.</summary>
    Private,
}

/// <summary>
/// What a player's Storm League games before this match say about them, worked out from
/// Heroes Profile's <c>players/matches</c> by <see cref="PlayerHistoryMath"/>. Only the summary
/// is kept on the focus card, never the games, and never anything from this match or later.
/// </summary>
public sealed class PlayerHistory
{
    [JsonConverter(typeof(JsonStringEnumConverter<PlayerHistoryStatus>))]
    public PlayerHistoryStatus Status { get; set; } = PlayerHistoryStatus.Unknown;

    public DateTime? FetchedAtUtc { get; set; }

    /// <summary>Storm League games before this match inside the lookback.</summary>
    public int Games { get; set; }

    /// <summary>Wins in a row (positive) or losses in a row (negative) up to this match.</summary>
    public int Streak { get; set; }

    /// <summary>The MMR change summed over <see cref="MmrGames"/> latest games, or null.</summary>
    public double? MmrChange { get; set; }

    /// <summary>How many games <see cref="MmrChange"/> adds up.</summary>
    public int MmrGames { get; set; }

    /// <summary>Of <see cref="Games"/>, those on this match's hero.</summary>
    public int HeroGames { get; set; }

    public int HeroWins { get; set; }

    /// <summary>This hero has the best win rate among the heroes with enough games lately.</summary>
    public bool BestHero { get; set; }

    /// <summary>This hero has the worst win rate among the heroes with enough games lately.</summary>
    public bool WorstHero { get; set; }

    [JsonIgnore]
    public bool IsKnown => Status == PlayerHistoryStatus.Known;

    /// <summary>The share of the latest games on this hero, 0 to 1.</summary>
    [JsonIgnore]
    public double HeroShare => Games <= 0 ? 0 : (double)HeroGames / Games;
}

public static class PlayerHistoryMath
{
    /// <summary>
    /// The summary of <paramref name="matches"/> for a player on <paramref name="hero"/> in the
    /// match <paramref name="replayId"/> played at <paramref name="playedAtUtc"/>. Only games
    /// strictly before that time count, and never the match itself, so no story can tell its result.
    /// </summary>
    public static PlayerHistory From(
        IEnumerable<PlayerMatch> matches,
        int? replayId,
        DateTime playedAtUtc,
        string hero,
        string attributeId,
        PlayerStorySettings settings,
        DateTime fetchedAtUtc
    )
    {
        settings ??= new PlayerStorySettings();
        List<PlayerMatch> before = (matches ?? Enumerable.Empty<PlayerMatch>())
            .Where(match =>
                match != null
                && match.ReplayId != replayId
                && match.PlayedAtUtc < playedAtUtc
                && playedAtUtc - match.PlayedAtUtc
                    <= TimeSpan.FromDays(Math.Max(1, settings.LookbackDays))
            )
            .GroupBy(match => match.ReplayId)
            .Select(group => group.First())
            .OrderByDescending(match => match.PlayedAtUtc)
            .ThenByDescending(match => match.ReplayId)
            .ToList();

        var history = new PlayerHistory
        {
            Status = PlayerHistoryStatus.Known,
            FetchedAtUtc = fetchedAtUtc,
            Games = before.Count,
        };
        if (before.Count == 0)
        {
            return history;
        }

        bool first = before[0].Won;
        int run = before.TakeWhile(match => match.Won == first).Count();
        history.Streak = first ? run : -run;

        List<double> changes = before
            .Take(Math.Max(1, settings.MmrGames))
            .Where(match => match.MmrChange is double change && !double.IsNaN(change))
            .Select(match => match.MmrChange.Value)
            .ToList();
        if (changes.Count > 0)
        {
            history.MmrChange = Math.Round(changes.Sum(), 1);
            history.MmrGames = changes.Count;
        }

        List<PlayerMatch> onHero = before
            .Where(match => SameHero(match, hero, attributeId))
            .ToList();
        history.HeroGames = onHero.Count;
        history.HeroWins = onHero.Count(match => match.Won);

        int minGames = Math.Max(1, settings.LatelyMinGames);
        var rates = before
            .Where(match => !string.IsNullOrWhiteSpace(match.AttributeId ?? match.Hero))
            .GroupBy(match => Key(match), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= minGames)
            .Select(group =>
                (group.Key, Rate: (double)group.Count(match => match.Won) / group.Count())
            )
            .ToList();
        string mine = onHero.Count > 0 ? Key(onHero[0]) : null;
        if (mine != null && rates.Count >= 2 && rates.Any(rate => Same(rate.Key, mine)))
        {
            double rate = rates.First(entry => Same(entry.Key, mine)).Rate;
            history.BestHero = rates.All(entry => Same(entry.Key, mine) || entry.Rate < rate);
            history.WorstHero = rates.All(entry => Same(entry.Key, mine) || entry.Rate > rate);
        }

        return history;
    }

    private static string Key(PlayerMatch match) =>
        string.IsNullOrWhiteSpace(match.AttributeId)
            ? match.Hero?.Trim()
            : match.AttributeId.Trim();

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameHero(PlayerMatch match, string hero, string attributeId)
    {
        if (
            !string.IsNullOrWhiteSpace(attributeId) && !string.IsNullOrWhiteSpace(match.AttributeId)
        )
        {
            return Same(match.AttributeId.Trim(), attributeId.Trim());
        }

        return !string.IsNullOrWhiteSpace(hero) && Same(match.Hero?.Trim(), hero.Trim());
    }
}
