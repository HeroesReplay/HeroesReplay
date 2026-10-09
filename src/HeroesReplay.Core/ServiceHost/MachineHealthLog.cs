using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The supervisor's machine line (#251): memory, commit charge, the leaking process counts, and
/// each watched process's private bytes, once per <see cref="MachineHealthSettings.LogInterval"/>.
/// A warning names each value above its limit, and while memory or commit is above its limit one
/// more names the processes holding the most commit, whoever owns them (#399).
/// </summary>
public sealed class MachineHealthLog
{
    private readonly MachineHealthSettings settings;
    private readonly Func<MachineHealthSnapshot> read;
    private readonly ILogger logger;
    private DateTimeOffset? lastLogged;

    public MachineHealthLog(MachineHealthSettings settings, ILogger<MachineHealthLog> logger)
        : this(settings, () => MachineHealthProbe.Read(settings), logger) { }

    internal MachineHealthLog(
        MachineHealthSettings settings,
        Func<MachineHealthSnapshot> read,
        ILogger logger
    )
    {
        this.settings = settings ?? new MachineHealthSettings();
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Logs when the interval has passed since the last line. True when it logged.</summary>
    public bool Tick(DateTimeOffset now)
    {
        TimeSpan interval =
            settings.LogInterval > TimeSpan.Zero ? settings.LogInterval : TimeSpan.FromHours(1);
        if (lastLogged is DateTimeOffset last && now - last < interval)
        {
            return false;
        }

        lastLogged = now;
        try
        {
            MachineHealthReport report = MachineHealth.Evaluate(read(), settings);
            logger.LogInformation("Machine: {Machine}.", MachineHealth.Describe(report));
            foreach (MachineProcessMemory process in report.Processes)
            {
                logger.LogInformation(
                    "Process {Name} pid {Pid}: {PrivateMegabytes} MB private, {WorkingSetMegabytes} MB working set, started {StartedAt:O}.",
                    process.Name,
                    process.Pid,
                    process.PrivateMegabytes,
                    process.WorkingSetMegabytes,
                    process.StartedAt
                );
            }

            foreach (string warning in report.Warnings)
            {
                logger.LogWarning("Machine health: {Warning}", warning);
            }

            if (report.TopConsumers.Count > 0)
            {
                // Whoever owns them (a browser tab held 13 GB on 2026-10-08, #399). Report only.
                logger.LogWarning(
                    "Machine health: the {Count} processes holding the most commit (private bytes), report only: {TopConsumers}.",
                    report.TopConsumers.Count,
                    string.Join("; ", report.TopConsumers.Select(MachineHealth.DescribeProcess))
                );
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the machine's health.");
        }

        return true;
    }
}
