using System;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Spectate's restart off a serviced .NET runtime (#450). Between replays the engine asks
/// <see cref="Due"/>; when the runtime spectate loaded is gone (its heartbeat's
/// <see cref="ServiceHeartbeat.CheckCurrentRuntime"/>: the folder, or a runtime library such as
/// the release unzip's zlib that did not load) and a supervisor runs, spectate stops after the
/// replay the way a release hand-off does: the stream stays up on the waiting scene, and the
/// supervisor starts spectate again on the current runtime. Without a supervisor nothing would
/// start it again, so it keeps running and says so once.
/// </summary>
public sealed class RuntimeRestart
{
    private readonly ILogger logger;
    private readonly Func<ServiceRoleRuntime> serviced;
    private readonly Func<bool> supervised;
    private bool warned;

    public RuntimeRestart(ILogger<RuntimeRestart> logger)
        : this(
            logger,
            ServiceHeartbeat.CheckCurrentRuntime,
            () => ServiceSupervisorFile.IsRunning()
        ) { }

    /// <param name="logger">Where the restart and the missing supervisor are logged.</param>
    /// <param name="serviced">The serviced runtime, or null while it is there.</param>
    /// <param name="supervised">True while a supervisor runs, in this session or another.</param>
    internal RuntimeRestart(
        ILogger logger,
        Func<ServiceRoleRuntime> serviced,
        Func<bool> supervised
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.serviced = serviced ?? throw new ArgumentNullException(nameof(serviced));
        this.supervised = supervised ?? throw new ArgumentNullException(nameof(supervised));
    }

    /// <summary>The runtime was serviced and a supervisor will start spectate again.</summary>
    public bool Due()
    {
        ServiceRoleRuntime runtime = serviced();
        if (runtime == null)
        {
            return false;
        }

        if (supervised())
        {
            return true;
        }

        if (!warned)
        {
            warned = true;
            logger.LogWarning(
                "Spectate runs on a serviced .NET runtime [{Code}]: {Cause} No supervisor runs to start it again, so it keeps running. Run `heroesreplay services stop`, then `heroesreplay services start --supervise`.",
                runtime.Code,
                runtime.Cause
            );
        }

        return false;
    }

    /// <summary>
    /// Spectate stops for the restart. Logged; true when it is still <see cref="Due"/>.
    /// </summary>
    public bool HandOff()
    {
        ServiceRoleRuntime runtime = serviced();
        if (runtime == null || !supervised())
        {
            return false;
        }

        logger.LogWarning(
            "Spectate runs on a serviced .NET runtime [{Code}]: {Cause} It stops between replays so the supervisor starts it again on the current runtime. A live stream stays up on the waiting scene.",
            runtime.Code,
            runtime.Cause
        );
        return true;
    }
}
