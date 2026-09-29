namespace HeroesReplay.Core.Services.Connectivity;

public static class ConnectivityResume
{
    public readonly record struct Decision(
        bool RetryHeroesProfile,
        bool StartStream,
        bool StopStream
    );

    /// <summary>
    /// OBS native reconnect owns a short outage. The internet edge does not stop or
    /// start the output, because StopStream would cancel that recovery. A stopped
    /// stream is repaired by reconcile while internet stays online.
    /// </summary>
    public static Decision Decide(
        bool wasOnline,
        bool isOnline,
        bool streamingEnabled,
        bool nativeReconnectOwnsTransient = true
    )
    {
        bool restored = !wasOnline && isOnline;
        bool lost = wasOnline && !isOnline;
        bool changeOutput = streamingEnabled && !nativeReconnectOwnsTransient;
        return new Decision(restored, restored && changeOutput, lost && changeOutput);
    }
}
