namespace HeroesReplay.Core.Replays;

public sealed class ReplayPathOptions
{
    public string Path { get; init; }
    public bool PlayOnce { get; init; } = true;

    /// <summary>The player to follow, Name#1234. Null keeps the normal camera.</summary>
    public string BattleTag { get; init; }

    /// <summary>Plays each replay as a Blind Rating request (#411): rank hidden, blind interface.</summary>
    public bool Blind { get; init; }
}
