namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public sealed class ExtensionWhoAmI
{
    public bool Reachable { get; init; }
    public int StatusCode { get; init; }
    public string TwitchLogin { get; init; }
    public string TwitchDisplayName { get; init; }
    public bool PlayerLinked { get; init; }
    public bool EntitlementActive { get; init; }
    public string Message { get; init; }
}
