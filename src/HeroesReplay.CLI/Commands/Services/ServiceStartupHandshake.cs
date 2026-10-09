using System;
using HeroesReplay.Core.ServiceHost;

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

    /// <summary>True ends the ready wait early: <c>services stop</c> asked everything to exit.</summary>
    public Func<bool> Cancelled { get; set; }

    /// <summary>
    /// The pid of the role process a launch started when the launcher did not report it (#397):
    /// a PowerShell that was cut off after <c>Start-Process</c> on a machine short of memory.
    /// Gets the pending record (its role, arguments, and nonce) and when the launch began. Null,
    /// or a result of null, means none was found.
    /// </summary>
    public Func<ServiceProcessRecord, DateTimeOffset, int?> FindStarted { get; set; }

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
