using System;
using System.IO;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

public enum ServiceStatusOutput
{
    Text,
    Json,
}

/// <summary>
/// What <c>services status</c> reads besides the lock and the process table. Tests replace each.
/// </summary>
public sealed class ServiceStatusQuery
{
    public ServiceStatusOutput Output { get; set; } = ServiceStatusOutput.Text;
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

    /// <summary>True while a supervisor holds its mutex.</summary>
    public Func<bool> SupervisorRunning { get; set; }

    public static ServiceStatusOutput ParseOutput(string value) =>
        string.Equals(value, "json", StringComparison.OrdinalIgnoreCase)
            ? ServiceStatusOutput.Json
            : ServiceStatusOutput.Text;
}
