using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace HeroesReplay.Core.Services.Processes;

public sealed class ServiceReadyReport
{
    public string Role { get; set; }
    public string Nonce { get; set; }
    public string Version { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
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
            ReadyAt = now,
            HeartbeatAt = now.AddTicks(1),
        };
    }

    public static void ReportFromEnvironment(string role)
    {
        string nonce = Environment.GetEnvironmentVariable(NonceVariable);
        if (!IsSafeNonce(nonce))
        {
            return;
        }

        string reportedRole = string.IsNullOrWhiteSpace(role)
            ? Environment.GetEnvironmentVariable(RoleVariable)
            : role;
        string version = Environment.GetEnvironmentVariable(VersionVariable);
        if (string.IsNullOrWhiteSpace(version))
        {
            version = CurrentVersion();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Report(
            new ServiceReadyReport
            {
                Role = reportedRole,
                Nonce = nonce,
                Version = version,
                ReadyAt = now,
            }
        );
    }

    public static void ReportHeartbeatFromEnvironment()
    {
        string nonce = Environment.GetEnvironmentVariable(NonceVariable);
        if (!IsSafeNonce(nonce))
        {
            return;
        }

        TryWriteHeartbeat(
            new ServiceProcessRecord
            {
                Name = Environment.GetEnvironmentVariable(RoleVariable),
                Nonce = nonce,
            },
            DateTimeOffset.UtcNow
        );
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

        DateTimeOffset heartbeat =
            at > existing.ReadyAt.Value ? at : existing.ReadyAt.Value.AddTicks(1);
        existing.HeartbeatAt = heartbeat;
        Report(existing, directory);
        return true;
    }

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

        try
        {
            string path = Path.Combine(directory ?? DefaultDirectory, record.Nonce + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            ServiceReadyReport report = JsonSerializer.Deserialize<ServiceReadyReport>(
                File.ReadAllText(path),
                Json
            );
            if (
                report == null
                || !string.Equals(report.Nonce, record.Nonce, StringComparison.Ordinal)
            )
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

    private static string CurrentVersion()
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
