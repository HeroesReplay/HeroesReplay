using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

/// <summary>
/// One spectated replay, parsed once. Snapshots reveal it as the match clock moves.
/// </summary>
public sealed class ExtensionGame
{
    public string GameMode { get; init; }
    public string Map { get; init; }
    public string GameVersion { get; init; }
    public IReadOnlyList<ExtensionPlayer> Players { get; init; } = [];
    public IReadOnlyList<ExtensionTalentPick> Talents { get; init; } = [];
}
