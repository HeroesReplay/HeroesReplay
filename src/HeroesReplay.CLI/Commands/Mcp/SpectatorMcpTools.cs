using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace HeroesReplay.CLI.Commands.Mcp;

/// <summary>
/// <see cref="SpectatorMcpTools.GetSpectatorStatus"/>: the snapshot, where it is, the game
/// process, and the machine as <c>services status</c> reads it (memory, commit, and above their
/// limits the processes holding the most commit, #399).
/// </summary>
public sealed record SpectatorStatusResult(
    SpectatorStatus Status,
    string StatusFile,
    GameProcessInfo GameProcess,
    MachineHealthReport Machine
);

/// <summary>The Heroes of the Storm processes by name, or the error that stopped the lookup.</summary>
public sealed record GameProcessInfo(
    string ProcessName,
    int Count,
    IReadOnlyList<int> Ids,
    string Error
);

/// <summary><see cref="SpectatorMcpTools.GetCurrentFocus"/>: the hero the spectator follows now.</summary>
public sealed record CurrentFocusResult(
    bool SpectatorRunning,
    bool SnapshotStale,
    string Phase,
    string Timer,
    SpectatorFocusStatus Focus
);

/// <summary>
/// Spectator and integration tools for agents. Each returns a typed object as MCP structured
/// content (with the same JSON as text for older clients), not JSON inside a string.
/// </summary>
[McpServerToolType]
public sealed class SpectatorMcpTools
{
    private readonly SpectatorStatusStore statusStore;

    public SpectatorMcpTools(SpectatorStatusStore statusStore)
    {
        this.statusStore = statusStore;
    }

    [
        McpServerTool(
            Name = "get_spectator_status",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description(
            "Live spectator snapshot: phase, timer, replay, focus, OBS session, and whether the snapshot is stale. Also the machine: memory, commit charge, and, above their limits, the processes holding the most commit (report only)."
        )
    ]
    public SpectatorStatusResult GetSpectatorStatus() =>
        new(statusStore.Read(), statusStore.FilePath, ProbeGameProcess(), ReadMachine());

    [
        McpServerTool(
            Name = "get_current_focus",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description("Currently selected hero/player according to the latest spectator snapshot.")
    ]
    public CurrentFocusResult GetCurrentFocus()
    {
        SpectatorStatus status = statusStore.Read();
        return new CurrentFocusResult(
            status.SpectatorRunning,
            status.SnapshotStale,
            status.Phase,
            status.Timer,
            status.Focus
        );
    }

    [
        McpServerTool(
            Name = "check_config",
            ReadOnly = true,
            Destructive = false,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description(
            "Bind appsettings and report which secrets are present without printing secret values."
        )
    ]
    public Task<CheckCommand.CheckResult> CheckConfig(CancellationToken cancellationToken) =>
        CheckCommand.RunTargetAsync("config", cancellationToken);

    [
        McpServerTool(
            Name = "check_heroesprofile",
            ReadOnly = true,
            Destructive = false,
            OpenWorld = true,
            UseStructuredContent = true
        ),
        Description(
            "Call Heroes Profile GET /replays (Kiota v1 Bearer) using the configured API key (supports op:// via 1Password CLI)."
        )
    ]
    public Task<CheckCommand.CheckResult> CheckHeroesProfile(CancellationToken cancellationToken) =>
        CheckCommand.RunTargetAsync("heroesprofile", cancellationToken);

    [
        McpServerTool(
            Name = "check_obs",
            ReadOnly = true,
            Destructive = false,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description("Connect to obs-websocket 5 and return the OBS Studio version.")
    ]
    public Task<CheckCommand.CheckResult> CheckObs(CancellationToken cancellationToken) =>
        CheckCommand.RunTargetAsync("obs", cancellationToken);

    [
        McpServerTool(
            Name = "check_twitch",
            ReadOnly = true,
            Destructive = false,
            OpenWorld = true,
            UseStructuredContent = true
        ),
        Description("Call Twitch Helix GetUsers for the configured channel.")
    ]
    public Task<CheckCommand.CheckResult> CheckTwitch(CancellationToken cancellationToken) =>
        CheckCommand.RunTargetAsync("twitch", cancellationToken);

    [
        McpServerTool(
            Name = "check_battlenet",
            ReadOnly = true,
            Destructive = false,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description("Read the Battle.net window and report whether the button says Play or Update.")
    ]
    public Task<CheckCommand.CheckResult> CheckBattleNet(CancellationToken cancellationToken) =>
        CheckCommand.RunTargetAsync("battlenet", cancellationToken);

    // Read-only, the same read as services status: no process is stopped or changed.
    private static MachineHealthReport ReadMachine()
    {
        try
        {
            MachineHealthSettings settings =
                ServiceCollectionExtensions.LoadMachineHealthSettings();
            return MachineHealth.Evaluate(MachineHealthProbe.Read(settings), settings);
        }
        catch (Exception e)
        {
            return new MachineHealthReport
            {
                Ok = false,
                Warnings = new[] { "The machine could not be read: " + e.Message },
            };
        }
    }

    private static GameProcessInfo ProbeGameProcess()
    {
        try
        {
            using var provider = new ServiceCollection()
                .AddCheckServices(CancellationToken.None)
                .BuildServiceProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            string name = settings.Process?.HeroesOfTheStorm ?? "HeroesOfTheStorm";
            Process[] processes = Process.GetProcessesByName(name);
            try
            {
                return new GameProcessInfo(
                    name,
                    processes.Length,
                    Array.ConvertAll(processes, p => p.Id),
                    null
                );
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception e)
        {
            return new GameProcessInfo(null, 0, Array.Empty<int>(), e.Message);
        }
    }
}
