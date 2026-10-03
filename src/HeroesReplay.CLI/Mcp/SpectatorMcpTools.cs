using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace HeroesReplay.CLI.Mcp;

/// <summary><see cref="SpectatorMcpTools.GetSpectatorStatus"/>: the snapshot, where it is, and the game process.</summary>
public sealed record SpectatorStatusResult(
    SpectatorStatus Status,
    string StatusFile,
    GameProcessInfo GameProcess
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
            "Live spectator snapshot: phase, timer, replay, focus, OBS session, and whether the snapshot is stale."
        )
    ]
    public SpectatorStatusResult GetSpectatorStatus() =>
        new(statusStore.Read(), statusStore.FilePath, ProbeGameProcess());

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
        CheckCommand.CheckConfigAsync(cancellationToken);

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
        CheckCommand.CheckHeroesProfileAsync(cancellationToken);

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
        CheckCommand.CheckObsAsync(cancellationToken);

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
        CheckCommand.CheckTwitchAsync(cancellationToken);

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
        CheckCommand.CheckBattleNetAsync(cancellationToken);

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
