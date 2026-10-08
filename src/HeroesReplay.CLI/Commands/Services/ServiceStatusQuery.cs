using System;
using System.IO;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// What <c>services status</c> reads besides the lock and the process table. Tests replace each.
/// </summary>
public sealed class ServiceStatusQuery
{
    public CliOutputFormat Output { get; set; } = CliOutputFormat.Text;
    public TimeProvider Time { get; set; }
    public ServiceHealthSettings Settings { get; set; }
    public Func<ServiceProcessRecord, ServiceReadyReport> ReadHeartbeat { get; set; }

    /// <summary>True while <c>services stop</c> has its stop file down.</summary>
    public Func<bool> StopRequested { get; set; }
    public string Environment { get; set; }
    public TextWriter Out { get; set; }

    /// <summary>Where the role log files are. Null means the default folder.</summary>
    public string LogDirectory { get; set; }

    /// <summary><c>supervisor.json</c>. Null reads no supervisor.</summary>
    public Func<ServiceSupervisorState> ReadSupervisor { get; set; }

    /// <summary>
    /// Whether a supervisor runs, and how that was decided (<see cref="ServiceSupervisorFile.Check"/>).
    /// </summary>
    public Func<ServiceSupervisorLiveness> SupervisorLiveness { get; set; }

    /// <summary>The machine section (#251). Null leaves it out.</summary>
    public Func<MachineHealthReport> ReadMachine { get; set; }
}
