namespace HeroesReplay.Core.Replays;

public sealed class ReplayPathOptions
{
    public string Path { get; init; }
    public bool PlayOnce { get; init; } = true;

    /// <summary>The player to follow, Name#1234. Null keeps the normal camera.</summary>
    public string BattleTag { get; init; }
}
