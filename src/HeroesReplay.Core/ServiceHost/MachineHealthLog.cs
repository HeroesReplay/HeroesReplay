using System;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The supervisor's machine line (#251): memory, commit charge, the leaking process counts, and
/// each watched process's private bytes, once per <see cref="MachineHealthSettings.LogInterval"/>.
/// A warning names each value above its limit.
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
                    "Process {Name} pid {Pid}: {PrivateMegabytes} MB private, started {StartedAt:O}.",
                    process.Name,
                    process.Pid,
                    process.PrivateMegabytes,
                    process.StartedAt
                );
            }

            foreach (string warning in report.Warnings)
            {
                logger.LogWarning("Machine health: {Warning}", warning);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the machine's health.");
        }

        return true;
    }
}
