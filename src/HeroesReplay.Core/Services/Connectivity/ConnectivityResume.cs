namespace HeroesReplay.Core.Services.Connectivity;

public static class ConnectivityResume
{
    public readonly record struct Decision(bool RetryHeroesProfile, bool StartStream);

    public static Decision Decide(bool wasOnline, bool isOnline, bool streamingEnabled)
    {
        bool restored = !wasOnline && isOnline;
        return new Decision(restored, restored && streamingEnabled);
    }
}
