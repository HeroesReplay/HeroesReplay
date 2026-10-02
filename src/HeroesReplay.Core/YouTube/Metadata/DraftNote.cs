namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// One unusual-draft note for one team. <see cref="Key"/> is stable (a role note's switch name,
/// such as <c>NoTank</c>, or a composition key, such as <c>DoubleSoak</c>) and looks up the
/// note's corpus frequency. <see cref="Text"/> is the configured wording, first letter upper case.
/// </summary>
public sealed record DraftNote(string Key, string Text);
