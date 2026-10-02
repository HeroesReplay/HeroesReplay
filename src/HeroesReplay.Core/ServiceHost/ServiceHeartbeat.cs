using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// A role's ready file, kept fresh. The role writes the file once when it is ready, then
/// rewrites it every <see cref="Interval"/> with its pid, path, version, readiness, the last
/// successful work, and the last error. <c>services status</c> reads the same file.
/// </summary>
/// <remarks>
/// The role's loops call <see cref="RecordWork"/>. Error logs reach <see cref="RecordError"/>
/// through <see cref="ServiceHeartbeatLoggerProvider"/>. Both do nothing in a process that
/// <c>services start</c> did not launch.
/// </remarks>
public sealed class ServiceHeartbeat : IDisposable
{
    private const int MaxErrorLength = 400;
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(access_token|api_token|api_key|apikey|token|key|secret|password)=([^&\s""']+)",
        RegexOptions.Compiled
    );
    private static readonly Regex BearerToken = new(
        @"(?i)\b(bearer|oauth:)\s*[A-Za-z0-9._\-]+",
        RegexOptions.Compiled
    );
    private static ServiceHeartbeat current;

    private readonly object gate = new();
    private readonly ServiceReadyReport report;
    private readonly TimeProvider time;
    private readonly string directory;
    private ITimer timer;
    private CancellationTokenRegistration stopRegistration;
    private bool closed;

    public ServiceHeartbeat(
        ServiceReadyReport identity,
        TimeSpan interval,
        TimeProvider time = null,
        string directory = null
    )
    {
        ArgumentNullException.ThrowIfNull(identity);
        Interval =
            interval > TimeSpan.Zero ? interval : ServiceHealthSettings.DefaultHeartbeatInterval;
        this.time = time ?? TimeProvider.System;
        this.directory = directory;
        report = new ServiceReadyReport
        {
            Role = identity.Role,
            Nonce = identity.Nonce,
            Version = identity.Version,
            ExecutablePath = identity.ExecutablePath,
            Pid = identity.Pid,
            HeartbeatIntervalSeconds = (int)Math.Ceiling(Interval.TotalSeconds),
        };
    }

    public TimeSpan Interval { get; }

    /// <summary>
    /// Report ready for the role <c>services start</c> launched this process as, then keep the
    /// heartbeat fresh until disposal. Null when no service nonce was given.
    /// </summary>
    public static ServiceHeartbeat StartFromEnvironment(
        string role,
        ServiceHealthSettings settings,
        CancellationToken stop
    )
    {
        string nonce = Environment.GetEnvironmentVariable(ServiceReadyFile.NonceVariable);
        if (!ServiceReadyFile.IsSafeNonce(nonce))
        {
            return null;
        }

        string version = Environment.GetEnvironmentVariable(ServiceReadyFile.VersionVariable);
        var heartbeat = new ServiceHeartbeat(
            new ServiceReadyReport
            {
                Role = string.IsNullOrWhiteSpace(role)
                    ? Environment.GetEnvironmentVariable(ServiceReadyFile.RoleVariable)
                    : role,
                Nonce = nonce,
                Version = string.IsNullOrWhiteSpace(version)
                    ? ServiceReadyFile.CurrentVersion()
                    : version,
                ExecutablePath = Environment.ProcessPath,
                Pid = Environment.ProcessId,
            },
            (settings ?? new ServiceHealthSettings()).Interval
        );
        heartbeat.Start(stop);
        heartbeat.Install();
        return heartbeat;
    }

    /// <summary>Makes this the heartbeat that <see cref="RecordWork"/> and <see cref="RecordError"/> reach.</summary>
    internal void Install() => Interlocked.Exchange(ref current, this);

    /// <summary>The running role did one unit of its work. No-op outside a service role.</summary>
    public static void RecordWork() => Volatile.Read(ref current)?.Work();

    /// <summary>The running role hit an error. No-op outside a service role.</summary>
    public static void RecordError(string message) => Volatile.Read(ref current)?.Error(message);

    /// <summary>Writes the ready file, the first heartbeat, then one every interval.</summary>
    public void Start(CancellationToken stop)
    {
        lock (gate)
        {
            report.Readiness = ServiceReadiness.Ready;
            report.ReadyAt = time.GetUtcNow();
            Write();
        }

        Beat();
        timer = time.CreateTimer(_ => Beat(), null, Interval, Interval);
        stopRegistration = stop.Register(MarkStopping);
    }

    public void Work()
    {
        lock (gate)
        {
            report.LastSuccessfulWorkAt = time.GetUtcNow();
        }
    }

    public void Error(string message)
    {
        lock (gate)
        {
            report.LastError = new ServiceRoleError
            {
                Message = Redact(message),
                At = time.GetUtcNow(),
            };
        }
    }

    /// <summary>The stop request reached the role. Status calls its exit stopped, not failed.</summary>
    public void MarkStopping()
    {
        lock (gate)
        {
            report.Readiness = ServiceReadiness.Stopping;
        }

        Beat();
    }

    public void Beat()
    {
        lock (gate)
        {
            if (closed || report.ReadyAt == null)
            {
                return;
            }

            report.HeartbeatAt = ServiceReadyFile.HeartbeatAfterReady(
                report.ReadyAt.Value,
                time.GetUtcNow()
            );
            Write();
        }
    }

    /// <summary>A copy of what the next write holds.</summary>
    public ServiceReadyReport Snapshot()
    {
        lock (gate)
        {
            return new ServiceReadyReport
            {
                Role = report.Role,
                Nonce = report.Nonce,
                Version = report.Version,
                ExecutablePath = report.ExecutablePath,
                Pid = report.Pid,
                Readiness = report.Readiness,
                ReadyAt = report.ReadyAt,
                HeartbeatAt = report.HeartbeatAt,
                HeartbeatIntervalSeconds = report.HeartbeatIntervalSeconds,
                LastSuccessfulWorkAt = report.LastSuccessfulWorkAt,
                LastError =
                    report.LastError == null
                        ? null
                        : new ServiceRoleError
                        {
                            Message = report.LastError.Message,
                            At = report.LastError.At,
                        },
            };
        }
    }

    /// <summary>Stops the refresh and writes the last heartbeat: stopping, or exited without a stop.</summary>
    public void Dispose()
    {
        timer?.Dispose();
        stopRegistration.Dispose();
        lock (gate)
        {
            if (!closed && report.ReadyAt != null)
            {
                if (report.Readiness == ServiceReadiness.Ready)
                {
                    report.Readiness = ServiceReadiness.Exited;
                }

                report.HeartbeatAt = ServiceReadyFile.HeartbeatAfterReady(
                    report.ReadyAt.Value,
                    time.GetUtcNow()
                );
                Write();
            }

            closed = true;
        }

        Interlocked.CompareExchange(ref current, null, this);
    }

    public static string Redact(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        string redacted = SecretAssignment.Replace(message, "$1=[redacted]");
        redacted = BearerToken.Replace(redacted, "$1 [redacted]");
        return redacted.Length <= MaxErrorLength ? redacted : redacted[..MaxErrorLength] + "...";
    }

    // Callers hold the gate. A status read can hold the file for a moment; the next beat retries.
    private void Write()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                ServiceReadyFile.Report(report, directory);
                return;
            }
            catch (IOException) when (attempt == 0)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
