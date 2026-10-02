using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Update;

/// <summary>
/// <c>update release-health</c>: reads <c>services.json</c>, the process table, each role's
/// heartbeat, and the stop file the way <c>services status</c> does, then asks
/// <see cref="ReleaseHealth.Judge"/>. Tests replace each source.
/// </summary>
public sealed record ReleaseHealthCheck
{
    public static readonly TimeSpan DefaultPoll = TimeSpan.FromSeconds(15);

    public string LockPath { get; init; } = ServiceLockStore.DefaultPath;

    /// <summary>The ready files. Null is <see cref="ServiceReadyFile.DefaultDirectory"/>.</summary>
    public string ReadyDirectory { get; init; }
    public string StopPath { get; init; } = ServiceStopFile.DefaultPath;
    public Func<int, string> ProcessNameOrNull { get; init; } = ProcessName;
    public Func<int, ServiceProcessProbe> Probe { get; init; } = ServiceProcessProbe.TryFromProcess;
    public ServiceHealthSettings Health { get; init; } = new();
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public TimeSpan Poll { get; init; } = DefaultPoll;
    public Action<TimeSpan> Wait { get; init; } = Thread.Sleep;
    public TextWriter Out { get; init; } = Console.Out;

    public ReleaseHealthResult Check(DateTimeOffset since, TimeSpan window)
    {
        DateTimeOffset now = Time.GetUtcNow();
        bool stopRequested = File.Exists(StopPath);
        ServiceStatusReport report = ServiceHealthClassifier.Build(
            ServiceLockStore.TryLoad(LockPath),
            ProcessNameOrNull,
            Probe,
            record => ServiceReadyFile.TryRead(record, ReadyDirectory),
            stopRequested,
            now,
            Health
        );
        return ReleaseHealth.Judge(report.Roles, stopRequested, since, now, window);
    }

    /// <summary>
    /// Checks every <see cref="Poll"/> until the install is healthy, the window closes, or a stop
    /// is requested. Prints the verdict each time it changes.
    /// </summary>
    public ReleaseHealthResult WaitForVerdict(
        DateTimeOffset since,
        TimeSpan window,
        CancellationToken cancellation
    )
    {
        string printed = null;
        while (true)
        {
            ReleaseHealthResult result = Check(since, window);
            string line = result.Describe();
            if (!string.Equals(line, printed, StringComparison.Ordinal))
            {
                Out.WriteLine($"{Time.GetUtcNow():HH:mm:ss}Z release health {line}");
                printed = line;
            }

            if (
                result.Verdict != ReleaseHealthVerdict.Waiting
                || cancellation.IsCancellationRequested
            )
            {
                return result;
            }

            Wait(Poll > TimeSpan.Zero ? Poll : DefaultPoll);
        }
    }

    private static string ProcessName(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
