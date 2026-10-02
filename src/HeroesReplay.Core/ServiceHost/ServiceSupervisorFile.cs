using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// What the running supervisor writes to <c>supervisor.json</c>: who it is, its rules, and
/// each supervised role's restarts. <c>services status</c> reads it.
/// </summary>
public sealed class ServiceSupervisorState
{
    public int Pid { get; set; }
    public string ExecutablePath { get; set; }
    public string Version { get; set; }
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

    /// <summary>True while a supervisor holds <paramref name="mutexName"/>.</summary>
    public static bool IsRunning(string mutexName = MutexName)
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
