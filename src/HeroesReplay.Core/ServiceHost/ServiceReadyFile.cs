using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The ready file of one role process, keyed by its nonce. The role writes it once when it is
/// ready, then refreshes it on every heartbeat (<see cref="ServiceHeartbeat"/>).
/// </summary>
public sealed class ServiceReadyReport
{
    public string Role { get; set; }
    public string Nonce { get; set; }
    public string Version { get; set; }
    public string ExecutablePath { get; set; }
    public int? Pid { get; set; }

    /// <summary><see cref="ServiceReadiness"/>: ready, stopping, or exited.</summary>
    public string Readiness { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }

    /// <summary>The interval the role refreshes this file on. Null in files from older roles.</summary>
    public int? HeartbeatIntervalSeconds { get; set; }

    /// <summary>Role-defined: match progress, a Twitch reconcile, a download pass, an upload pass.</summary>
    public DateTimeOffset? LastSuccessfulWorkAt { get; set; }
    public ServiceRoleError LastError { get; set; }

    /// <summary>
    /// Spectate only: replay sessions in a row that ended without match progress (no match clock,
    /// no award screen). Null for the other roles and before the first session ends.
    /// </summary>
    public int? SessionsWithoutProgress { get; set; }

    /// <summary>Spectate only: how the last replay session ended (<c>LoadTimedOut</c>, ...).</summary>
    public string LastOutcome { get; set; }

    /// <summary>
    /// Spectate only: how many replay sessions this process ended with each outcome. The release
    /// health gate reads it to tell a build that cannot play from a stack with nothing to play.
    /// </summary>
    public Dictionary<string, int> SessionOutcomes { get; set; }

    /// <summary>
    /// Spectate only: when the current replay's launch and loading phase began. Null outside it
    /// (the report, a hold, an idle queue, an outage) and once the match clock moves. A launch
    /// that stays here too long is <c>spectate.launch_stalled</c>.
    /// </summary>
    public DateTimeOffset? LaunchingSince { get; set; }
}

public sealed class ServiceRoleError
{
    public string Message { get; set; }
    public DateTimeOffset? At { get; set; }
}

/// <summary>What a role says about itself in <see cref="ServiceReadyReport.Readiness"/>.</summary>
public static class ServiceReadiness
{
    public const string Ready = "ready";

    /// <summary>The stop file or Ctrl+C reached the role. It is shutting down on purpose.</summary>
    public const string Stopping = "stopping";

    /// <summary>The role left its main loop without a stop request.</summary>
    public const string Exited = "exited";
}

public static class ServiceReadyFile
{
    public const string NonceVariable = "HEROESREPLAY_SERVICE_NONCE";
    public const string RoleVariable = "HEROESREPLAY_SERVICE_ROLE";
    public const string VersionVariable = "HEROESREPLAY_SERVICE_VERSION";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "ready"
        );

    public static ServiceReadyReport Immediate(ServiceProcessRecord record)
    {
        if (record == null)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ServiceReadyReport
        {
            Role = record.Name,
            Nonce = record.Nonce,
            Version = record.Version,
            Readiness = ServiceReadiness.Ready,
            ReadyAt = now,
            HeartbeatAt = now.AddTicks(1),
        };
    }

    public static bool TryWriteHeartbeat(
        ServiceProcessRecord record,
        DateTimeOffset at,
        string directory = null
    )
    {
        ServiceReadyReport existing = TryRead(record, directory);
        if (existing?.ReadyAt == null)
        {
            return false;
        }

        existing.HeartbeatAt = HeartbeatAfterReady(existing.ReadyAt.Value, at);
        Report(existing, directory);
        return true;
    }

    /// <summary>A heartbeat always follows the ready write, even when the clock has not moved.</summary>
    public static DateTimeOffset HeartbeatAfterReady(DateTimeOffset readyAt, DateTimeOffset at) =>
        at > readyAt ? at : readyAt.AddTicks(1);

    public static void Report(ServiceReadyReport report, string directory = null)
    {
        if (report == null || !IsSafeNonce(report.Nonce))
        {
            return;
        }

        string folder = directory ?? DefaultDirectory;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, report.Nonce + ".json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(report, Json));
        File.Move(temp, path, overwrite: true);
    }

    public static ServiceReadyReport TryRead(ServiceProcessRecord record, string directory = null)
    {
        if (record == null || !IsSafeNonce(record.Nonce))
        {
            return null;
        }

        string path = Path.Combine(directory ?? DefaultDirectory, record.Nonce + ".json");
        // The role replaces this file on every heartbeat. A read that meets the replace retries.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return Read(record, path);
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

    private static ServiceReadyReport Read(ServiceProcessRecord record, string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        ServiceReadyReport report = JsonSerializer.Deserialize<ServiceReadyReport>(
            File.ReadAllText(path),
            Json
        );
        if (report == null || !string.Equals(report.Nonce, record.Nonce, StringComparison.Ordinal))
        {
            return null;
        }

        if (
            !string.IsNullOrWhiteSpace(record.Name)
            && !string.IsNullOrWhiteSpace(report.Role)
            && !string.Equals(report.Role, record.Name, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return report;
    }

    public static void Delete(string nonce, string directory = null)
    {
        if (!IsSafeNonce(nonce))
        {
            return;
        }

        string path = Path.Combine(directory ?? DefaultDirectory, nonce + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static bool IsSafeNonce(string nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > 64)
        {
            return false;
        }

        foreach (char character in nonce)
        {
            if (!char.IsLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public static string CurrentVersion()
    {
        Assembly assembly = typeof(ServiceReadyFile).Assembly;
        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
