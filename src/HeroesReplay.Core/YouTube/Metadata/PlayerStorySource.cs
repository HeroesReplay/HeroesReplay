using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroFocus.Cards;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>One player's history as a title story may use it: the hero, never the player.</summary>
public sealed record PlayerStoryFacts(
    int Team,
    string Hero,
    PlayerHistory History,
    int? CareerGames = null
);

/// <summary>
/// The players' histories for a replay, read from its focus card when the spectator loads it
/// (the card goes once the replay is spectated). Private and unread players are left out. Empty
/// when player stories are off or the replay has no card.
/// </summary>
public static class PlayerStorySource
{
    public static IReadOnlyList<PlayerStoryFacts> For(AppSettings settings, int? replayId)
    {
        if (
            settings?.YouTube?.Titles?.PlayerStories?.Enabled != true
            || replayId is not int id
            || string.IsNullOrWhiteSpace(settings.Location?.DataDirectory)
        )
        {
            return Array.Empty<PlayerStoryFacts>();
        }

        try
        {
            FocusCard card = new FocusCardStore(
                FocusCardStore.DirectoryFor(settings.Location.DataDirectory)
            ).Read(id.ToString(CultureInfo.InvariantCulture));
            return From(card);
        }
        catch (Exception e)
            when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            return Array.Empty<PlayerStoryFacts>();
        }
    }

    public static IReadOnlyList<PlayerStoryFacts> From(FocusCard card) =>
        (card?.Players ?? new List<FocusCardPlayer>())
            .Where(player =>
                !player.IsAi
                && !string.IsNullOrWhiteSpace(player.Hero)
                && player.History?.IsKnown == true
                && player.Strength?.Status != FocusStrengthStatus.Private
            )
            .Select(player => new PlayerStoryFacts(
                player.Team,
                player.Hero.Trim(),
                player.History,
                player.Strength?.IsKnown == true ? player.Strength.Games : null
            ))
            .ToList();
}
