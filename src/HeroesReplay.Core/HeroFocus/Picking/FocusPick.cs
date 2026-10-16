namespace HeroesReplay.Core.HeroFocus.Picking;

/// <summary>
/// The player the cycle follows for a whole focus replay (#433), checked against the parsed
/// replay. The stream and YouTube show only <see cref="Hero"/>, <see cref="Build"/> and
/// <see cref="Role"/>; <see cref="Player"/> stays on this machine.
/// </summary>
/// <param name="ReplayId">The focus replay's id.</param>
/// <param name="Key">The replay's focus card and ledger key.</param>
/// <param name="PlayerIndex">The player's index in the replay (the spectator's 1-0 key).</param>
/// <param name="Player"><c>Name#1234</c>; local only.</param>
/// <param name="Hero">The catalog hero's English name.</param>
/// <param name="AttributeId">The hero's four-letter attribute id.</param>
/// <param name="Role">The catalog role: Tank, Bruiser, Healer, Support or an Assassin.</param>
/// <param name="Build">The guide build's name when the talents clearly match it, else null.</param>
public sealed record FocusPick(
    int ReplayId,
    string Key,
    int PlayerIndex,
    string Player,
    string Hero,
    string AttributeId,
    string Role,
    string Build
)
{
    /// <summary><c>Li-Ming (Teleport Build)</c>, or the hero alone without a clear build.</summary>
    public string Label => Describe(Hero, Build);

    public static string Describe(string hero, string build) =>
        string.IsNullOrWhiteSpace(hero) ? null
        : string.IsNullOrWhiteSpace(build) ? hero.Trim()
        : $"{hero.Trim()} ({build.Trim()})";
}
