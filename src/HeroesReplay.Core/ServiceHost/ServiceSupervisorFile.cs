using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// What the running supervisor writes to <c>supervisor.json</c>: who it is, its rules, and
/// each supervised role's restarts. <c>services status</c> reads it.
/// </summary>
public sealed class ServiceSupervisorState
{
    public int Pid { get; set; }

    /// <summary>
    /// When the supervisor process started (<see cref="ProcessTable"/>). A reader that cannot see
    /// the mutex matches it against the live pid, so a reused pid does not count. Null in a file
    /// an older build wrote.
    /// </summary>
    public DateTimeOffset? ProcessStartedAt { get; set; }
    public string ExecutablePath { get; set; }
    public string Version { get; set; }

    /// <summary>When supervision began, after the process started.</summary>
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The stop file reached the supervisor. It restarts nothing more and exits.</summary>
    public bool Stopping { get; set; }
    public string LogPath { get; set; }
    public List<long> BackoffSeconds { get; set; } = new();
    public int Budget { get; set; }
    public long BudgetWindowSeconds { get; set; }
    public long StaleRestartAfterSeconds { get; set; }
    public List<string> Supervised { get; set; } = new();
    public List<ServiceRoleRestarts> Roles { get; set; } = new();
}

public static class ServiceSupervisorFile
{
    /// <summary>One supervisor per Windows session. It is held for the supervisor's lifetime.</summary>
    public const string MutexName = @"Local\HeroesReplay.ServiceSupervisor";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "supervisor.json"
        );

    public static void Save(string path, ServiceSupervisorState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }

    public static ServiceSupervisorState TryLoad(string path)
    {
        // The supervisor replaces the file on each pass. A read that meets the replace retries.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<ServiceSupervisorState>(
                    File.ReadAllText(path),
                    Json
                );
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The supervisor writes its file at least every heartbeat interval and just before each
    /// restart, whose ready wait is at most 45 s. A file older than this many intervals is a hung
    /// or dead supervisor's.
    /// </summary>
    public const int FreshForIntervals = 4;

    // The same allowance as a role's recorded start time (ServiceProcessPlan).
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    public static TimeSpan FreshFor(ServiceHealthSettings health) =>
        (health ?? new ServiceHealthSettings()).Interval * FreshForIntervals;

    /// <summary>
    /// True while a supervisor runs: this session sees <paramref name="mutexName"/>, or
    /// <c>supervisor.json</c> passes <see cref="FromStateFile"/>.
    /// </summary>
    public static bool IsRunning(string mutexName = MutexName, string statePath = null) =>
        Check(mutexName, statePath).Running;

    /// <summary>
    /// Whether a supervisor runs, and how that was decided. The mutex decides when this session
    /// can see it. A <c>Local\</c> mutex exists per logon session, so an SSH session never sees
    /// the desktop's supervisor (#283); then <c>supervisor.json</c> decides.
    /// </summary>
    public static ServiceSupervisorLiveness Check(
        string mutexName = MutexName,
        string statePath = null,
        TimeSpan? freshFor = null
    )
    {
        if (MutexVisible(mutexName))
        {
            return ServiceSupervisorLiveness.ByMutex;
        }

        return FromStateFile(
            TryLoad(statePath ?? DefaultPath),
            ProcessTable.Find,
            DateTimeOffset.UtcNow,
            freshFor ?? FreshFor(null)
        );
    }

    /// <summary>
    /// The file counts as a running supervisor only when it was written within
    /// <paramref name="freshFor"/> and its pid is alive with the start time it recorded. A file
    /// from a build that recorded no start time counts when the live pid started no later than
    /// supervision began: a reused pid starts after the supervisor exited.
    /// </summary>
    public static ServiceSupervisorLiveness FromStateFile(
        ServiceSupervisorState state,
        Func<int, ProcessTableEntry> findProcess,
        DateTimeOffset now,
        TimeSpan freshFor
    )
    {
        if (state == null || state.Pid <= 0)
        {
            return ServiceSupervisorLiveness.None;
        }

        TimeSpan age = now - state.UpdatedAt;
        if (age > freshFor)
        {
            return ServiceSupervisorLiveness.NotRunning(
                $"it was last written {ServiceHealthClassifier.Describe(age)} ago, and the mutex is not visible from this session"
            );
        }

        ProcessTableEntry process = findProcess?.Invoke(state.Pid);
        if (process == null)
        {
            return ServiceSupervisorLiveness.NotRunning($"no process has pid {state.Pid}");
        }

        if (process.StartTime is not DateTimeOffset started)
        {
            return ServiceSupervisorLiveness.NotRunning(
                $"the start time of pid {state.Pid} cannot be read from this session"
            );
        }

        bool same = state.ProcessStartedAt is DateTimeOffset recorded
            ? (started - recorded).Duration() <= StartTimeTolerance
            : started <= state.StartedAt;
        if (!same)
        {
            return ServiceSupervisorLiveness.NotRunning(
                $"pid {state.Pid} is another process now (started {started.ToLocalTime():yyyy-MM-dd HH:mm:ss})"
            );
        }

        return ServiceSupervisorLiveness.ByStateFile;
    }

    private static bool MutexVisible(string mutexName)
    {
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out Mutex existing))
            {
                return false;
            }

            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>
/// Whether a supervisor runs and how that was decided, for <c>services status</c>.
/// </summary>
public sealed record ServiceSupervisorLiveness
{
    public const string ViaMutex = "mutex";
    public const string ViaStateFile = "supervisor.json";
    public const string MutexNotVisible = "mutex not visible from this session";

    public static ServiceSupervisorLiveness ByMutex { get; } =
        new() { Running = true, SeenVia = ViaMutex };

    public static ServiceSupervisorLiveness ByStateFile { get; } =
        new()
        {
            Running = true,
            SeenVia = ViaStateFile,
            Detail = MutexNotVisible,
        };

    /// <summary>No mutex and no state file: nothing to report.</summary>
    public static ServiceSupervisorLiveness None { get; } = new();

    public bool Running { get; init; }

    /// <summary>
    /// <see cref="ViaMutex"/> or <see cref="ViaStateFile"/>. Null when neither showed a supervisor
    /// to check.
    /// </summary>
    public string SeenVia { get; init; }

    /// <summary>Why the state file did or did not count. Null when the mutex decided.</summary>
    public string Detail { get; init; }

    public static ServiceSupervisorLiveness NotRunning(string detail) =>
        new() { SeenVia = ViaStateFile, Detail = detail };
}

/// <summary>
/// The single-instance claim of a supervisor. Dispose it on the thread that acquired it.
/// </summary>
public sealed class ServiceSupervisorMutex : IDisposable
{
    private readonly Mutex mutex;
    private bool released;

    private ServiceSupervisorMutex(Mutex mutex)
    {
        this.mutex = mutex;
    }

    /// <summary>The claim, or null when another supervisor holds it.</summary>
    public static ServiceSupervisorMutex TryAcquire(
        string mutexName = ServiceSupervisorFile.MutexName
    )
    {
        var mutex = new Mutex(false, mutexName);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // The last supervisor died holding it. The claim passes to this one.
        }

        return new ServiceSupervisorMutex(mutex);
    }

    public void Dispose()
    {
        if (!released)
        {
            released = true;
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException) { }
        }

        mutex.Dispose();
    }
}
