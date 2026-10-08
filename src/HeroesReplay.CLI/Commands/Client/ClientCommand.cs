using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.GameClient.Firewall;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Client;

public class ClientCommand : Command
{
    public ClientCommand()
        : base(
            "client",
            "Configure the Heroes of the Storm client for spectating (windowed 1080p + AhliObs)."
        )
    {
        Subcommands.Add(ConfigureCommand());
        Subcommands.Add(StatusCommand());
        Subcommands.Add(FirewallCommand());
    }

    private static Command ConfigureCommand()
    {
        var command = new Command(
            "configure",
            "Write Variables.txt (windowed 1080p, AhliObs) and copy the StormInterface into Documents."
        );
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await Task.Run(() => RunConfigure(), cancellationToken);
            }
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Report whether Variables.txt and AhliObs match the spectator preset."
        );
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code (client.preset_ok, client.preset_mismatch, client.error), message, environment, details (variablesPath, interfaceInstalled, matchesPreset, mismatches[], hotSRunning)."
        );
        command.Options.Add(output);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                CliResult<ClientStatusResult> status = await Task.Run(
                    () => ReadStatus(ReadClientStatus),
                    cancellationToken
                );
                TextWriter stdout = CliOutput.Out(parseResult);
                return CliOutput.Format(parseResult, output) == CliOutputFormat.Json
                    ? CliOutput.WriteJson(status, stdout)
                    : WriteStatus(status, stdout, CliOutput.Error(parseResult));
            }
        );
        return command;
    }

    private static Command FirewallCommand()
    {
        var command = new Command(
            "firewall",
            "Add an inbound Windows Firewall rule for each installed Heroes client exe. Needs an elevated shell; spectate itself does not."
        );
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await Task.Run(() => RunFirewall(), cancellationToken);
            }
        );
        return command;
    }

    private static int RunFirewall()
    {
        if (!MediumIntegrityProcess.IsCurrentProcessElevated())
        {
            Console.Error.WriteLine(
                "Adding firewall rules needs an elevated shell. Run `heroesreplay client firewall` as administrator."
            );
            return 1;
        }

        using ServiceProvider provider = CreateProvider();
        AppSettings settings = provider.GetRequiredService<AppSettings>();
        IGameFirewall firewall = provider.GetRequiredService<IGameFirewall>();
        IReadOnlyList<InstalledClient> clients = InstalledClientCatalog.Clients(
            settings.Location?.GameInstallDirectory
        );
        var paths = new List<string>(clients.Count);
        foreach (InstalledClient client in clients)
        {
            paths.Add(client.ExePath);
        }

        int failed = 0;
        foreach (FirewallRuleOutcome outcome in firewall.AllowInboundClients(paths))
        {
            Console.WriteLine(
                string.IsNullOrEmpty(outcome.AllowedBy)
                    ? $"{outcome.State}: {outcome.ProgramPath}"
                    : $"{outcome.State}: {outcome.ProgramPath} by {outcome.AllowedBy}"
            );
            if (
                outcome.State is FirewallRuleState.Failed or FirewallRuleState.MissingNeedsElevation
            )
            {
                failed++;
            }
        }

        Console.WriteLine($"{paths.Count} installed client(s), {failed} without a rule.");
        return failed == 0 ? 0 : 1;
    }

    private static int RunConfigure()
    {
        using ServiceProvider provider = CreateProvider();
        StormClientConfigurator configurator =
            provider.GetRequiredService<StormClientConfigurator>();
        ClientConfigureResult result = configurator.Configure();

        Console.WriteLine($"Variables: {result.VariablesPath}");
        Console.WriteLine(
            result.InterfaceCopied
                ? $"Interface: copied {result.InterfaceSource} -> {result.InterfaceDestination}"
                : $"Interface: not copied ({result.InterfaceDestination})"
        );

        foreach (string warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }

        Console.WriteLine(
            "Quit Heroes of the Storm before configure if it is running; the client overwrites Variables.txt on exit."
        );
        return result.InterfaceCopied ? 0 : 1;
    }

    public const string PresetOk = "client.preset_ok";
    public const string PresetMismatch = "client.preset_mismatch";
    public const string StatusError = "client.error";

    private static ClientStatusResult ReadClientStatus()
    {
        using ServiceProvider provider = CreateProvider();
        return provider.GetRequiredService<StormClientConfigurator>().GetStatus();
    }

    /// <summary>
    /// <c>client status</c>: ok (exit 0) only when Variables.txt and AhliObs match the
    /// spectator preset (<see cref="PresetOk"/>); a mismatch is <see cref="PresetMismatch"/>, and
    /// a status that could not be read is <see cref="StatusError"/>.
    /// </summary>
    public static CliResult<ClientStatusResult> ReadStatus(Func<ClientStatusResult> read)
    {
        try
        {
            ClientStatusResult status = read();
            return new CliResult<ClientStatusResult>
            {
                Ok = status.MatchesPreset,
                Code = status.MatchesPreset ? PresetOk : PresetMismatch,
                Message = status.MatchesPreset
                    ? "Preset: matches windowed 1080p + AhliObs."
                    : "Preset: mismatch. " + string.Join("; ", status.Mismatches),
                Environment = CliJson.CurrentEnvironment(),
                Details = status,
            };
        }
        catch (Exception e)
        {
            return new CliResult<ClientStatusResult>
            {
                Ok = false,
                Code = StatusError,
                Message = "The client status could not be read. " + e.Message,
                Environment = CliJson.CurrentEnvironment(),
            };
        }
    }

    private static int WriteStatus(
        CliResult<ClientStatusResult> result,
        TextWriter output,
        TextWriter error
    )
    {
        ClientStatusResult status = result.Details;
        if (status == null)
        {
            error.WriteLine(result.Message);
            return 1;
        }

        output.WriteLine($"Variables: {status.VariablesPath}");
        output.WriteLine($"AhliObs installed: {status.InterfaceInstalled}");
        output.WriteLine($"HotS running: {status.HotSRunning}");
        if (status.MatchesPreset)
        {
            output.WriteLine("Preset: matches windowed 1080p + AhliObs.");
            return 0;
        }

        output.WriteLine("Preset: mismatch");
        foreach (string mismatch in status.Mismatches)
        {
            output.WriteLine($"  {mismatch}");
        }

        return 1;
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection().AddClientServices().BuildHeroesReplayProvider();
    }
}
