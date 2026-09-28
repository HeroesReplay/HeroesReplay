namespace HeroesReplay.Core.Models;

public sealed class ReplayPathOptions
{
    public string Path { get; init; }
    public bool PlayOnce { get; init; } = true;

    /// <summary>Observe slot 0-9. 0 is hotkey 1 and 9 is hotkey 0. Null keeps the normal camera.</summary>
    public int? PlayerIndex { get; init; }
}
