using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Runs a role's <see cref="IServiceDependencyProbe"/> (#305): once before the role reports ready,
/// so the ready file already carries the result, then every
/// <see cref="ServiceHealthSettings.DependencyProbeInterval"/> (10 min) while it passes and every
/// <see cref="ServiceHealthSettings.DependencyRetryInterval"/> (2 min) while it fails, never
/// faster than a minute. Each probe is bounded by
/// <see cref="ServiceHealthSettings.DependencyProbeTimeout"/>. A probe that times out or throws is
/// unreachable, never an exception for the role. A passing probe clears the degraded state.
/// </summary>
public sealed class ServiceDependencyMonitor
{
    private readonly IServiceDependencyProbe probe;
    private readonly ServiceHealthSettings settings;
    private readonly ILogger logger;
    private readonly TimeProvider time;
    private readonly bool enabled;
    private ServiceDependencyResult last;

    /// <param name="probe">The role's probe.</param>
    /// <param name="settings">Intervals, the bound, and the switch.</param>
    /// <param name="logger">One line when the result changes. Never a token.</param>
    /// <param name="time">The clock and the delays.</param>
    /// <param name="serviceRole">
    /// Null asks the process: only a role <c>services start</c> launched probes, because only it
    /// has a heartbeat to report in.
    /// </param>
    public ServiceDependencyMonitor(
        IServiceDependencyProbe probe,
        ServiceHealthSettings settings,
        ILogger logger = null,
        TimeProvider time = null,
        bool? serviceRole = null
    )
    {
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        this.settings = settings ?? new ServiceHealthSettings();
        this.logger = logger ?? NullLogger.Instance;
        this.time = time ?? TimeProvider.System;
        enabled =
            this.settings.DependencyProbes
            && (serviceRole ?? ServiceHeartbeat.LaunchedAsServiceRole);
    }

    /// <summary>The last result, or null before the first probe.</summary>
    public ServiceDependencyResult Last => Volatile.Read(ref last);

    /// <summary>
    /// The probe before the ready write. Null when probes are off or this process is not a
    /// service role, and when the role is stopping.
    /// </summary>
    public async Task<ServiceDependencyResult> FirstAsync(CancellationToken stop)
    {
        if (!enabled)
        {
            return null;
        }

        string unused = probe.NotUsedReason;
        if (!string.IsNullOrWhiteSpace(unused))
        {
            return Remember(ServiceDependencyResult.Unused(probe.Dependency, unused));
        }

        return await ProbeAsync(stop).ConfigureAwait(false);
    }

    /// <summary>
    /// One bounded probe. Null only when <paramref name="stop"/> ended it: a stopping role reports
    /// nothing new.
    /// </summary>
    public async Task<ServiceDependencyResult> ProbeAsync(CancellationToken stop)
    {
        TimeSpan bound = settings.DependencyProbeBound();
        ServiceDependencyResult result;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stop);
        bounded.CancelAfter(bound);
        try
        {
            // WaitAsync also ends a probe that ignores its token.
            result =
                await probe
                    .CheckAsync(bounded.Token)
                    .WaitAsync(bound + TimeSpan.FromSeconds(1), time, stop)
                    .ConfigureAwait(false)
                ?? probe.Unreachable($"The {probe.Dependency} probe returned nothing.");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            result = probe.Unreachable(
                $"{probe.Dependency} did not answer within {ServiceHealthClassifier.Describe(bound)}."
            );
        }
        catch (Exception e)
        {
            result = probe.Unreachable(
                $"The {probe.Dependency} probe failed: {ServiceHeartbeat.Redact(e.Message)}"
            );
        }

        return Remember(result);
    }

    /// <summary>
    /// Probes again after each wait and records every result with <paramref name="record"/>
    /// (<see cref="ServiceHeartbeat.RecordDependency"/>) until <paramref name="stop"/>. Does
    /// nothing when probes are off, or the role does not use the dependency.
    /// </summary>
    public async Task RunAsync(Action<ServiceDependencyResult> record, CancellationToken stop)
    {
        if (!enabled || !string.IsNullOrWhiteSpace(probe.NotUsedReason))
        {
            return;
        }

        record ??= ServiceHeartbeat.RecordDependency;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(settings.NextDependencyProbe(Last?.Failed == true), time, stop)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ServiceDependencyResult result = await ProbeAsync(stop).ConfigureAwait(false);
            if (result == null)
            {
                return;
            }

            record(result);
        }
    }

    /// <summary>
    /// <see cref="RunAsync"/> in the background until the returned handle is disposed or
    /// <paramref name="stop"/> fires.
    /// </summary>
    public IDisposable Watch(CancellationToken stop)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task loop = Task.Run(
            () => RunAsync(ServiceHeartbeat.RecordDependency, linked.Token),
            CancellationToken.None
        );
        return new Watcher(linked, loop);
    }

    private ServiceDependencyResult Remember(ServiceDependencyResult result)
    {
        ServiceDependencyResult previous = Interlocked.Exchange(ref last, result);
        bool changed =
            previous == null
            || !string.Equals(previous.State, result.State, StringComparison.Ordinal)
            || !string.Equals(previous.Code, result.Code, StringComparison.Ordinal);
        if (!changed)
        {
            return result;
        }

        if (result.Failed)
        {
            logger.LogWarning(
                "{Dependency} probe: {State} [{Code}]. {Cause} The role stays up, degraded; the supervisor does not restart it. Fix: {Remediation}",
                result.Dependency,
                result.State,
                result.Code,
                result.Cause,
                result.Remediation
            );
        }
        else if (previous?.Failed == true)
        {
            logger.LogInformation(
                "{Dependency} probe: {State}. {Cause} [{Previous}] is cleared.",
                result.Dependency,
                result.State,
                result.Cause,
                previous.Code
            );
        }
        else
        {
            logger.LogInformation(
                "{Dependency} probe: {State}. {Cause}",
                result.Dependency,
                result.State,
                result.Cause
            );
        }

        return result;
    }

    private sealed class Watcher(CancellationTokenSource linked, Task loop) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                linked.Cancel();
                loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // RunAsync does not throw; a stop mid-probe is not a failure.
            }
            catch (ObjectDisposedException) { }

            linked.Dispose();
        }
    }
}
