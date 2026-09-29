using System;
using HeroesReplay.Core.Services.Processes;

namespace HeroesReplay.CLI.Commands.Services;

public sealed class ServiceStartupHandshake
{
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(200);
    public Action<TimeSpan> Wait { get; set; }
    public Func<ServiceProcessRecord, ServiceReadyReport> TryReadReady { get; set; }
    public Action<int> StopStarted { get; set; }
    public Func<int, ServiceProcessProbe> Probe { get; set; }
    public Action<string> Report { get; set; }
    public string Version { get; set; }
    public SpectateStartupFacts Spectate { get; set; }
    public TwitchStartupFacts Twitch { get; set; }
    public DownloadStartupFacts Download { get; set; }
    public YouTubeStartupFacts YouTube { get; set; }
    public ServiceProcessRecord Pending { get; set; }

    public static ServiceStartupHandshake ReadyNow()
    {
        return new ServiceStartupHandshake
        {
            TryReadReady = ServiceReadyFile.Immediate,
            Wait = _ => { },
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };
    }
}
