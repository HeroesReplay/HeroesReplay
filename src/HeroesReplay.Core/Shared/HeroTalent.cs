namespace HeroesReplay.Core.Shared;

/// <summary>
/// One talent of a hero, from the heroes-data catalog (#412). A replay names a pick by
/// <see cref="TalentId"/> (the talent's element id, <c>WizardTeleportCalamity</c>), so the pick
/// maps back to its tier, its column in the talent panel, and its English name.
/// </summary>
/// <param name="TalentId">The talent element id the replay's <c>TalentChosen</c> event names.</param>
/// <param name="ButtonId">The button element id. It differs from the talent id for some heroes.</param>
/// <param name="Tier">The tier, 0 (first talent tier) to 6 (last). Chromie's tiers unlock at other levels.</param>
/// <param name="Column">The 1-based position in the tier, left to right, as the game shows it.</param>
/// <param name="Name">The English name, such as <c>Calamity</c>.</param>
/// <param name="Icon">The icon file name.</param>
public sealed record HeroTalent(
    string TalentId,
    string ButtonId,
    int Tier,
    int Column,
    string Name,
    string Icon
);
