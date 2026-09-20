using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace HeroesReplay.CLI.Mcp;

[McpServerToolType]
public sealed class SpectatorMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SpectatorStatusStore statusStore;

    public SpectatorMcpTools(SpectatorStatusStore statusStore)
    {
        this.statusStore = statusStore;
    }

    [
        McpServerTool(Name = "get_spectator_status"),
        Description(
            "Live spectator snapshot: phase, timer, replay, focus, OBS session, and whether the snapshot is stale."
        )
    ]
    public string GetSpectatorStatus()
    {
        SpectatorStatus status = statusStore.Read();
        return JsonSerializer.Serialize(
            new
            {
                status,
                statusFile = statusStore.FilePath,
                gameProcess = ProbeGameProcess(),
            },
            JsonOptions
        );
    }

    [
        McpServerTool(Name = "get_current_focus"),
        Description("Currently selected hero/player according to the latest spectator snapshot.")
    ]
    public string GetCurrentFocus()
    {
        SpectatorStatus status = statusStore.Read();
        return JsonSerializer.Serialize(
            new
            {
                status.SpectatorRunning,
                status.SnapshotStale,
                status.Phase,
                status.Timer,
                status.Focus,
            },
            JsonOptions
        );
    }

    [
        McpServerTool(Name = "check_config"),
        Description(
            "Bind appsettings and report which secrets are present without printing secret values."
        )
    ]
    public Task<string> CheckConfig(CancellationToken cancellationToken) =>
        RunCheck(() => CheckCommand.CheckConfigAsync(cancellationToken));

    [
        McpServerTool(Name = "check_heroesprofile"),
        Description(
            "Call Heroes Profile GET /replays (Kiota v1 Bearer) using the configured API key (supports op:// via 1Password CLI)."
        )
    ]
    public Task<string> CheckHeroesProfile(CancellationToken cancellationToken) =>
        RunCheck(() => CheckCommand.CheckHeroesProfileAsync(cancellationToken));

    [
        McpServerTool(Name = "check_obs"),
        Description("Connect to obs-websocket 5 and return the OBS Studio version.")
    ]
    public Task<string> CheckObs(CancellationToken cancellationToken) =>
        RunCheck(() => CheckCommand.CheckObsAsync(cancellationToken));

    [
        McpServerTool(Name = "check_twitch"),
        Description("Call Twitch Helix GetUsers for the configured channel.")
    ]
    public Task<string> CheckTwitch(CancellationToken cancellationToken) =>
        RunCheck(() => CheckCommand.CheckTwitchAsync(cancellationToken));

    private static async Task<string> RunCheck(Func<Task<CheckCommand.CheckResult>> action)
    {
        CheckCommand.CheckResult result = await action();
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static object ProbeGameProcess()
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
                return new
                {
                    processName = name,
                    count = processes.Length,
                    ids = Array.ConvertAll(processes, p => p.Id),
                };
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
            return new { error = e.Message };
        }
    }
}
