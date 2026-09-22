using System;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class ConnectivitySnapshot
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public bool Internet { get; init; }
    public bool Twitch { get; init; }
    public bool TwitchProbed { get; init; } = true;
    public bool HeroesProfile { get; init; }

    public bool Healthy => Internet && HeroesProfile && (!TwitchProbed || Twitch);

    public string Describe()
    {
        string twitch = TwitchProbed ? (Twitch ? "ok" : "fail") : "skipped";
        return $"internet={(Internet ? "ok" : "fail")} twitch={twitch} heroesprofile={(HeroesProfile ? "ok" : "fail")}";
    }
}

public sealed class ConnectivityChangedEventArgs : EventArgs
{
    public bool IsOnline { get; init; }
    public ConnectivitySnapshot Snapshot { get; init; }
}
