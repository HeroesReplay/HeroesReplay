using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace HeroesReplay.CLI.Commands.Otel;

public class OtelCommand : Command
{
    public OtelCommand()
        : base(
            "otel",
            "Optional Aspire Dashboard (Docker) for OpenTelemetry traces, metrics, and logs."
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
            "Start the Aspire Dashboard container (UI :18888, OTLP gRPC :4317)."
        );
        command.SetAction(
            async (parseResult, cancellationToken) =>
                await Task.Run(() => RunCompose("up", "-d"), cancellationToken)
        );
        return command;
    }

    private static Command DownCommand()
    {
        var command = new Command("down", "Stop the Aspire Dashboard container.");
        command.SetAction(
            async (parseResult, cancellationToken) =>
                await Task.Run(() => RunCompose("down"), cancellationToken)
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Show compose status and the OTLP endpoint the CLI uses."
        );
        command.SetAction(
            async (parseResult, cancellationToken) => await Task.Run(PrintStatus, cancellationToken)
        );
        return command;
    }

    private static int PrintStatus()
    {
        string file = FindComposeFile();
        Console.WriteLine($"Compose: {file ?? "(not found)"}");
        Console.WriteLine("Dashboard UI: http://127.0.0.1:18888");
        Console.WriteLine(
            $"OTLP gRPC: {Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? HeroesReplayOpenTelemetry.DefaultOtlpEndpoint}"
        );
        if (file == null)
        {
            return 1;
        }

        return RunProcess("docker", $"compose -f \"{file}\" ps");
    }

    private static int RunCompose(params string[] composeArgs)
    {
        string file = FindComposeFile();
        if (file == null)
        {
            Console.Error.WriteLine(
                "Could not find deploy/aspire/docker-compose.yml or Assets/aspire/docker-compose.yml."
            );
            return 1;
        }

        string args = $"compose -f \"{file}\" {string.Join(' ', composeArgs)}";
        int exit = RunProcess("docker", args);
        if (exit == 0 && composeArgs.Length > 0 && composeArgs[0] == "up")
        {
            Console.WriteLine("Aspire Dashboard UI: http://127.0.0.1:18888");
            Console.WriteLine("OTLP gRPC:           http://127.0.0.1:4317");
            Console.WriteLine(
                "Point the CLI with OTEL_EXPORTER_OTLP_ENDPOINT if you change ports."
            );
        }

        return exit;
    }

    private static int RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                }
            );
            process?.WaitForExit();
            return process?.ExitCode ?? 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Failed to run {fileName}: {e.Message}");
            return 1;
        }
    }

    private static string FindComposeFile()
    {
        string fileName = Path.Combine("aspire", "docker-compose.yml");
        foreach (
            string candidate in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "deploy", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", fileName),
            }
        )
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        DirectoryInfo dir = new(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string repo = Path.Combine(dir.FullName, "deploy", "aspire", "docker-compose.yml");
            if (File.Exists(repo))
            {
                return repo;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
