using System;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class ConnectivitySnapshot
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public bool Internet { get; init; }
    public bool Twitch { get; init; }
    public bool HeroesProfile { get; init; }

    public bool Healthy => Internet && Twitch && HeroesProfile;

    public string Describe() =>
        $"internet={(Internet ? "ok" : "fail")} twitch={(Twitch ? "ok" : "fail")} heroesprofile={(HeroesProfile ? "ok" : "fail")}";
}

public sealed class ConnectivityChangedEventArgs : EventArgs
{
    public bool IsOnline { get; init; }
    public ConnectivitySnapshot Snapshot { get; init; }
}
