using System;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The Heroes Profile player lookups behind focus-hero picks (#433), <c>HeroesProfileApi:PlayerStats</c>:
/// a player's Storm League record on a hero (<c>players/heroes</c>, 50,000 calls a week and 60 a
/// minute on our plan, measured 2026-10-16), cached under <c>Data\HeroesProfile\players</c>, and the
/// daily privacy feed (<c>players/privacy/changes</c>) that purges players who went private, as the
/// terms require.
/// </summary>
public sealed class PlayerStatsSettings
{
    /// <summary>A known record is used for this long before it is looked up again.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A private, missing, or refused player or hero is not asked about again for this long.</summary>
    public TimeSpan UnknownRetryAfter { get; set; } = TimeSpan.FromDays(1);

    /// <summary>The most lookups in any 7 days, well under the endpoint's 50,000.</summary>
    public int WeeklyCallBudget { get; set; } = 15000;

    /// <summary>How long one lookup's job may run.</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the privacy feed is read. The terms ask for at least daily.</summary>
    public TimeSpan PrivacyInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How long the housekeeping pass keeps a player's private mark, which tells a focus card to
    /// drop that player's numbers (#492). Other player files go once older than <see cref="MaxAge"/>.
    /// </summary>
    public TimeSpan PrivateKeep { get; set; } = TimeSpan.FromDays(30);
}
