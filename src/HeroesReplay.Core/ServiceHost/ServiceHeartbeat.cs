using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HeroesReplay.Core.ServiceHost.Logs;

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
    /// <param name="role">The role name.</param>
    /// <param name="settings">The heartbeat interval.</param>
    /// <param name="stop">The role's stop.</param>
    /// <param name="dependency">
    /// The first dependency probe (#305), so the ready file already says when the role starts
    /// degraded. Null writes none.
    /// </param>
    public static ServiceHeartbeat StartFromEnvironment(
        string role,
        ServiceHealthSettings settings,
        CancellationToken stop,
        ServiceDependencyResult dependency = null
    )
    {
        if (!LaunchedAsServiceRole)
        {
            return null;
        }

        string nonce = Environment.GetEnvironmentVariable(ServiceReadyFile.NonceVariable);
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
        if (dependency != null)
        {
            heartbeat.Dependency(dependency);
        }

        heartbeat.Start(stop);
        heartbeat.Install();
        return heartbeat;
    }

    /// <summary>
    /// <c>services start</c> or the supervisor launched this process as a role: it has a service
    /// nonce, so it writes a heartbeat.
    /// </summary>
    public static bool LaunchedAsServiceRole =>
        ServiceReadyFile.IsSafeNonce(
            Environment.GetEnvironmentVariable(ServiceReadyFile.NonceVariable)
        );

    /// <summary>The role's last dependency probe (#305). No-op outside a service role.</summary>
    public static void RecordDependency(ServiceDependencyResult result) =>
        Volatile.Read(ref current)?.Dependency(result);

    /// <summary>
    /// Writes the last dependency probe. <see cref="ServiceRoleDependency.Since"/> keeps the first
    /// time of the same state and code in a row.
    /// </summary>
    public void Dependency(ServiceDependencyResult result)
    {
        if (result == null)
        {
            return;
        }

        lock (gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            ServiceRoleDependency previous = report.Dependency;
            bool same =
                previous != null
                && string.Equals(previous.State, result.State, StringComparison.Ordinal)
                && string.Equals(previous.Code, result.Code, StringComparison.Ordinal);
            report.Dependency = new ServiceRoleDependency
            {
                Name = result.Dependency,
                State = result.State,
                Code = result.Code,
                Cause = Redact(result.Cause),
                Remediation = result.Remediation,
                CheckedAt = now,
                Since = same && previous.Since is DateTimeOffset since ? since : now,
            };
        }
    }

    /// <summary>Makes this the heartbeat that <see cref="RecordWork"/> and <see cref="RecordError"/> reach.</summary>
    internal void Install() => Interlocked.Exchange(ref current, this);

    /// <summary>The running role did one unit of its work. No-op outside a service role.</summary>
    public static void RecordWork() => Volatile.Read(ref current)?.Work();

    /// <summary>
    /// A spectate replay session ended. No-op outside a service role. See <see cref="Session"/>.
    /// </summary>
    public static void RecordSession(bool matchProgress, string outcome) =>
        Volatile.Read(ref current)?.Session(matchProgress, outcome);

    /// <summary>The running role hit an error. No-op outside a service role.</summary>
    public static void RecordError(string message) => Volatile.Read(ref current)?.Error(message);

    /// <summary>
    /// Spectate is launching or loading a replay, or its client is still busy with game data.
    /// No-op outside a service role. See <see cref="Launching"/>.
    /// </summary>
    public static void RecordLaunching() => Volatile.Read(ref current)?.Launching();

    /// <summary>Spectate left its launch phase (the report). No-op outside a service role.</summary>
    public static void RecordLaunchEnded() => Volatile.Read(ref current)?.LaunchEnded();

    /// <summary>
    /// Spectate holds the next replay while the desired stream is down (#396), or stopped holding
    /// (<paramref name="since"/> null). No-op outside a service role. See <see cref="StreamHold"/>.
    /// </summary>
    public static void RecordStreamHold(DateTimeOffset? since, string reason) =>
        Volatile.Read(ref current)?.StreamHold(since, reason);

    /// <summary>
    /// The hold is not work, and not a launch: it neither moves <c>lastSuccessfulWorkAt</c> nor
    /// counts toward a stalled launch or sessions without progress.
    /// </summary>
    public void StreamHold(DateTimeOffset? since, string reason)
    {
        lock (gate)
        {
            report.StreamHoldSince = since;
            report.StreamHoldReason = since == null ? null : Redact(reason);
        }
    }

    /// <summary>
    /// The launch phase starts now, or starts over: a client that is still downloading or
    /// preparing game data is at work, so its wait does not count toward a stalled launch.
    /// </summary>
    public void Launching()
    {
        lock (gate)
        {
            report.LaunchingSince = time.GetUtcNow();
        }
    }

    /// <summary>The launch phase is over: the match clock moved, the report began, or the session ended.</summary>
    public void LaunchEnded()
    {
        lock (gate)
        {
            report.LaunchingSince = null;
        }
    }

    /// <summary>
    /// The role reports a concern about its own work (degraded), or clears it with null.
    /// No-op outside a service role.
    /// </summary>
    public static void RecordConcern(string code, string cause) =>
        Volatile.Read(ref current)?.Concern(code, cause);

    /// <summary>Sets the concern, or clears it when <paramref name="code"/> is blank.</summary>
    public void Concern(string code, string cause)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                report.Concern = null;
                return;
            }

            DateTimeOffset since =
                string.Equals(report.Concern?.Code, code, StringComparison.Ordinal)
                && report.Concern.Since is DateTimeOffset earlier
                    ? earlier
                    : time.GetUtcNow();
            report.Concern = new ServiceRoleConcern
            {
                Code = code,
                Cause = Redact(cause),
                Since = since,
            };
        }
    }

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

    /// <summary>Work also ends a run of spectate sessions without match progress, and the launch phase.</summary>
    public void Work()
    {
        lock (gate)
        {
            report.LastSuccessfulWorkAt = time.GetUtcNow();
            report.LaunchingSince = null;
            if (report.SessionsWithoutProgress != null)
            {
                report.SessionsWithoutProgress = 0;
            }
        }
    }

    /// <summary>
    /// A spectate replay session ended with <paramref name="outcome"/>. Match progress (the match
    /// clock, or the award screen) is work and ends the run of sessions without it. Any other end
    /// (a defer, a hold, a load timeout, a crash) is not work and adds one to that run. Every end
    /// is also counted by outcome (<see cref="ServiceReadyReport.SessionOutcomes"/>).
    /// </summary>
    public void Session(bool matchProgress, string outcome)
    {
        lock (gate)
        {
            report.LastOutcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome;
            report.LaunchingSince = null;
            report.SessionOutcomes ??= new Dictionary<string, int>(StringComparer.Ordinal);
            string key = report.LastOutcome ?? "None";
            report.SessionOutcomes[key] = report.SessionOutcomes.GetValueOrDefault(key) + 1;
            if (matchProgress)
            {
                report.LastSuccessfulWorkAt = time.GetUtcNow();
                report.SessionsWithoutProgress = 0;
            }
            else
            {
                report.SessionsWithoutProgress = (report.SessionsWithoutProgress ?? 0) + 1;
            }
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
                SessionsWithoutProgress = report.SessionsWithoutProgress,
                LastOutcome = report.LastOutcome,
                SessionOutcomes =
                    report.SessionOutcomes == null
                        ? null
                        : new Dictionary<string, int>(report.SessionOutcomes),
                LaunchingSince = report.LaunchingSince,
                StreamHoldSince = report.StreamHoldSince,
                StreamHoldReason = report.StreamHoldReason,
                Concern =
                    report.Concern == null
                        ? null
                        : new ServiceRoleConcern
                        {
                            Code = report.Concern.Code,
                            Cause = report.Concern.Cause,
                            Since = report.Concern.Since,
                        },
                Dependency = report.Dependency?.Copy(),
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

    public static string Redact(string message) =>
        ServiceLogRedaction.Redact(message, MaxErrorLength);

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
