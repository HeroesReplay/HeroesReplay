using System.CommandLine;
using System.Threading.Tasks;

namespace HeroesReplay.CLI.Commands.Otel;

public class OtelCommand : Command
{
    public OtelCommand()
        : base(
            "otel",
            "Standalone Aspire dashboard for OpenTelemetry traces, metrics, and logs. No Docker."
        )
    {
        Subcommands.Add(UpCommand());
        Subcommands.Add(DownCommand());
        Subcommands.Add(StatusCommand());
    }

    private static Command UpCommand()
    {
        var command = new Command(
            "up",
            "Start the Aspire dashboard with the Aspire CLI (UI :18888, OTLP gRPC :4317)."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                bool listening = AspireDashboardHost.EnsureRunning();
                return Task.FromResult(listening ? 0 : 1);
            }
        );
        return command;
    }

    private static Command DownCommand()
    {
        var command = new Command(
            "down",
            "Stop the Aspire dashboard process started by heroesreplay."
        );
        command.SetAction(
            (parseResult, cancellationToken) => Task.FromResult(AspireDashboardHost.StopRunning())
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Show whether the Aspire dashboard is listening and the OTLP endpoint the CLI uses."
        );
        command.SetAction(
            (parseResult, cancellationToken) => Task.FromResult(AspireDashboardHost.PrintStatus())
        );
        return command;
    }
}
