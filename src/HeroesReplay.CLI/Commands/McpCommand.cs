using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Mcp;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace HeroesReplay.CLI.Commands;

public class McpCommand : Command
{
    public McpCommand()
        : base(
            "mcp",
            "Run a stdio MCP server so an agent can read spectator status and run integration checks. Logs go to stderr."
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
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<SpectatorMcpTools>();

        using IHost host = builder.Build();
        await host.RunAsync(cancellationToken);
    }
}
