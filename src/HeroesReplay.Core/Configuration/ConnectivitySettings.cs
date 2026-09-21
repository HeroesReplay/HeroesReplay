using System;

namespace HeroesReplay.Core.Configuration;

public class ConnectivitySettings
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
    public int FailThreshold { get; set; } = 3;
    public int RecoverThreshold { get; set; } = 2;
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public string InternetHost { get; set; } = "1.1.1.1";
    public Uri TwitchUri { get; set; } = new("https://www.twitch.tv");
    public Uri HeroesProfileUri { get; set; } = new("https://www.heroesprofile.com");
}
