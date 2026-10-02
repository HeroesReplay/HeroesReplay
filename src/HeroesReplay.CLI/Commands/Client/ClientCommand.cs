using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
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
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await Task.Run(() => RunStatus(), cancellationToken);
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
            Console.WriteLine($"{outcome.State}: {outcome.ProgramPath}");
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

    private static int RunStatus()
    {
        using ServiceProvider provider = CreateProvider();
        StormClientConfigurator configurator =
            provider.GetRequiredService<StormClientConfigurator>();
        ClientStatusResult status = configurator.GetStatus();

        Console.WriteLine($"Variables: {status.VariablesPath}");
        Console.WriteLine($"AhliObs installed: {status.InterfaceInstalled}");
        Console.WriteLine($"HotS running: {status.HotSRunning}");
        if (status.MatchesPreset)
        {
            Console.WriteLine("Preset: matches windowed 1080p + AhliObs.");
            return 0;
        }

        Console.WriteLine("Preset: mismatch");
        foreach (string mismatch in status.Mismatches)
        {
            Console.WriteLine($"  {mismatch}");
        }

        return 1;
    }

    private static ServiceProvider CreateProvider()
    {
        return new ServiceCollection().AddClientServices().BuildHeroesReplayProvider();
    }
}
