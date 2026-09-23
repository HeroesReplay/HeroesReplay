using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

/// <summary>
/// One full-game body, without <c>game_id</c> or <c>seq</c>. Those are added when it is posted.
/// </summary>
public sealed class ExtensionSnapshot
{
    public string Phase { get; init; }
    public string GameMode { get; init; }
    public string Map { get; init; }
    public string GameVersion { get; init; }
    public IReadOnlyList<ExtensionSnapshotPlayer> Players { get; init; } = [];
}
