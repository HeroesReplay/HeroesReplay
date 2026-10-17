using System;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace HeroesReplay.CLI.Commands.Mcp;

public class McpCommand : Command
{
    /// <summary>
    /// The MCP SDK's own options (camelCase, nulls left out) with its resolver first and then
    /// <see cref="McpToolsJsonContext"/>, so a tool's schema, arguments and result need no
    /// reflection (#454). Options without a resolver, or a tool type missing from the context,
    /// throw when the host starts.
    /// </summary>
    public static readonly JsonSerializerOptions ToolJsonOptions = new(
        McpJsonUtilities.DefaultOptions
    )
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            McpJsonUtilities.DefaultOptions.TypeInfoResolver,
            McpToolsJsonContext.Default
        ),
    };

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
            .WithTools<SpectatorMcpTools>(ToolJsonOptions)
            .WithTools<ObsMcpTools>(ToolJsonOptions);

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken);
    }
}
