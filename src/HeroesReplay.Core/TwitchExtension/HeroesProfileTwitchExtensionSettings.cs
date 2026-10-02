using System;

namespace HeroesReplay.Core.TwitchExtension;

public class HeroesProfileTwitchExtensionSettings
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; }
    public string TwitchUserName { get; set; }
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(8);
}
