using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Mcp;

public class McpCommand : Command
{
    public McpCommand()
        : base(
            "mcp",
            "Run a stdio MCP server so an agent can read spectator status, run integration checks, and inspect, validate, and screenshot OBS read-only. Logs go to stderr."
        )
    {
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await RunAsync(cancellationToken);
            }
        );
    }

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Services.AddSingleton<SpectatorStatusStore>();

        // The OBS tools open their own short read-only session per call and read settings
        // each time, so a config change or an OBS restart needs no MCP restart.
        builder.Services.AddSingleton<IObsReadSessionFactory, ObsWebsocketReadSessionFactory>();
        builder.Services.AddSingleton<Func<ObsInspectionSettings>>(
            ServiceCollectionExtensions.LoadObsInspectionSettings
        );
        builder
            .Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<SpectatorMcpTools>()
            .WithTools<ObsMcpTools>();

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken);
    }
}
