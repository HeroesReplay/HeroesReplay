using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Telemetry;

/// <summary>
/// The <c>OpenTelemetry</c> section (#538): what each process exports, how much it buffers, and
/// how the local Aspire dashboard is kept running and kept small. Every value has a default, so a
/// file without the section behaves as before, only lighter.
/// </summary>
public sealed class TelemetrySettings
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; } = true;
    public string OtlpEndpoint { get; set; }
    public string ServiceName { get; set; }

    public TelemetryTraceSettings Traces { get; set; } = new();
    public TelemetryMetricSettings Metrics { get; set; } = new();
    public TelemetryLogSettings Logs { get; set; } = new();
    public TelemetryExportSettings Export { get; set; } = new();
    public TelemetryDashboardSettings Dashboard { get; set; } = new();
}

public sealed class TelemetryTraceSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The share of new root traces kept, 0 to 1. A child span follows its parent's decision, so a
    /// replay's trace is kept or dropped whole. 1 keeps every trace.
    /// </summary>
    public double SampleRatio { get; set; } = 1.0;

    /// <summary>A span for every outgoing HTTP call (Heroes Profile, YouTube, Twitch).</summary>
    public bool HttpClient { get; set; } = true;

    public double EffectiveSampleRatio =>
        double.IsNaN(SampleRatio) ? 1.0 : Math.Clamp(SampleRatio, 0.0, 1.0);
}

public sealed class TelemetryMetricSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often metrics are exported. The SDK default is 60 s.</summary>
    public TimeSpan ExportInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>GC, thread pool, and JIT metrics (<c>AddRuntimeInstrumentation</c>).</summary>
    public bool Runtime { get; set; } = true;

    /// <summary>HTTP client duration histograms per host.</summary>
    public bool HttpClient { get; set; } = true;

    public int EffectiveExportIntervalMilliseconds =>
        (int)
            Math.Clamp(
                ExportInterval.TotalMilliseconds,
                TimeSpan.FromSeconds(5).TotalMilliseconds,
                TimeSpan.FromHours(1).TotalMilliseconds
            );
}

public sealed class TelemetryLogSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The lowest level sent to the dashboard. The role log files keep their own level
    /// (<c>ServiceLogs</c>), so this only trims what the dashboard holds.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool IncludeScopes { get; set; }
}

/// <summary>
/// The batch each signal keeps in memory before it is sent. The SDK default queue is 2,048 items
/// per signal, allocated up front, and items queue while the dashboard is down.
/// </summary>
public sealed class TelemetryExportSettings
{
    public int MaxQueueSize { get; set; } = 512;
    public int MaxExportBatchSize { get; set; } = 256;
    public TimeSpan ScheduledDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public int EffectiveMaxQueueSize => Math.Max(16, MaxQueueSize);

    public int EffectiveMaxExportBatchSize =>
        Math.Clamp(MaxExportBatchSize, 1, EffectiveMaxQueueSize);

    public int ScheduledDelayMilliseconds =>
        (int)Math.Clamp(ScheduledDelay.TotalMilliseconds, 100, 60_000);

    public int TimeoutMilliseconds => (int)Math.Clamp(Timeout.TotalMilliseconds, 1_000, 60_000);
}

/// <summary>
/// The local Aspire dashboard (#538): the supervisor relaunches it when OTLP stops listening, and
/// these limits keep it small. Every limit goes to the dashboard as
/// <c>Dashboard:TelemetryLimits:*</c>; the GC values go to its process as <c>DOTNET_*</c>.
/// </summary>
public sealed class TelemetryDashboardSettings
{
    public static readonly IReadOnlyList<TimeSpan> DefaultRestartDelays = new[]
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    };

    /// <summary>The supervisor probes OTLP and relaunches the dashboard when it is gone.</summary>
    public bool Supervise { get; set; } = true;

    /// <summary>How often the supervisor probes the OTLP port.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The waits between relaunches while it stays down; the last one repeats. The first relaunch
    /// is at the first probe that finds it down.
    /// </summary>
    public List<TimeSpan> RestartDelays { get; set; }

    /// <summary>Traces held in memory. The dashboard default is 10,000 (8.5 GB on the stream PC, 2026-10-08).</summary>
    public int MaxTraceCount { get; set; } = 1000;

    /// <summary>Structured log entries held, per resource. The dashboard default is 10,000.</summary>
    public int MaxLogCount { get; set; } = 2000;

    /// <summary>Metric data points held, per resource. The dashboard default is 50,000.</summary>
    public int MaxMetricsCount { get; set; } = 10000;

    /// <summary>Attributes kept per log, span, or point. The dashboard default is 128.</summary>
    public int MaxAttributeCount { get; set; } = 64;

    /// <summary>Characters kept per attribute value. The dashboard default is unlimited.</summary>
    public int MaxAttributeLength { get; set; } = 2048;

    /// <summary>Events kept per span. The dashboard default is unlimited.</summary>
    public int MaxSpanEventCount { get; set; } = 64;

    /// <summary>
    /// Workstation GC (<c>DOTNET_gcServer=0</c>). Server GC keeps a heap per core and returns
    /// memory to Windows late; one small dashboard does not need it.
    /// </summary>
    public bool WorkstationGc { get; set; } = true;

    /// <summary><c>DOTNET_GCConserveMemory</c>, 0 to 9. Higher compacts harder to stay small. 0 is off.</summary>
    public int GcConserveMemory { get; set; } = 5;

    /// <summary>
    /// <c>DOTNET_GCHeapHardLimit</c> in MB. 0 sets none. With a limit, a dashboard that reaches it
    /// fails and the supervisor relaunches it empty.
    /// </summary>
    public int GcHeapHardLimitMb { get; set; }

    public IReadOnlyList<TimeSpan> EffectiveRestartDelays =>
        RestartDelays?.Where(delay => delay > TimeSpan.Zero).ToList() is { Count: > 0 } delays
            ? delays
            : DefaultRestartDelays;

    public TimeSpan EffectiveCheckInterval =>
        CheckInterval < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : CheckInterval;

    /// <summary>The waits after <paramref name="failedRelaunches"/> relaunches that did not bring it back.</summary>
    public TimeSpan DelayFor(int failedRelaunches)
    {
        if (failedRelaunches <= 0)
        {
            return TimeSpan.Zero;
        }

        IReadOnlyList<TimeSpan> delays = EffectiveRestartDelays;
        return delays[Math.Min(failedRelaunches - 1, delays.Count - 1)];
    }

    /// <summary>The <c>--Dashboard:TelemetryLimits:*</c> arguments; a value of 0 or less is left at the dashboard's default.</summary>
    public IReadOnlyList<string> DashboardArguments()
    {
        var arguments = new List<string>();
        Add(arguments, "MaxTraceCount", MaxTraceCount);
        Add(arguments, "MaxLogCount", MaxLogCount);
        Add(arguments, "MaxMetricsCount", MaxMetricsCount);
        Add(arguments, "MaxAttributeCount", MaxAttributeCount);
        Add(arguments, "MaxAttributeLength", MaxAttributeLength);
        Add(arguments, "MaxSpanEventCount", MaxSpanEventCount);
        return arguments;
    }

    /// <summary>The <c>DOTNET_*</c> GC variables for the dashboard process.</summary>
    public IReadOnlyDictionary<string, string> ProcessEnvironment()
    {
        var environment = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (WorkstationGc)
        {
            environment["DOTNET_gcServer"] = "0";
        }

        if (GcConserveMemory > 0)
        {
            environment["DOTNET_GCConserveMemory"] = Math.Min(9, GcConserveMemory)
                .ToString(CultureInfo.InvariantCulture);
        }

        if (GcHeapHardLimitMb > 0)
        {
            long bytes = GcHeapHardLimitMb * 1024L * 1024L;
            // The runtime reads this variable as hexadecimal.
            environment["DOTNET_GCHeapHardLimit"] = "0x" + bytes.ToString("X", CultureInfo.InvariantCulture);
        }

        return environment;
    }

    private static void Add(List<string> arguments, string name, int value)
    {
        if (value > 0)
        {
            arguments.Add(
                $"--Dashboard:TelemetryLimits:{name}={value.ToString(CultureInfo.InvariantCulture)}"
            );
        }
    }
}
