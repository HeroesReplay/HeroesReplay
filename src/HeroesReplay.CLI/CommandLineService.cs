using System;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands;
using HeroesReplay.Core.Services.Shared;

namespace HeroesReplay.CLI;

public class CommandLineService
{
    private readonly IAdminChecker adminChecker;

    public CommandLineService(IAdminChecker adminChecker)
    {
        this.adminChecker = adminChecker;
    }

    public async Task<int> InvokeAsync(string[] args)
    {
        var root = new HeroesReplayCommand();
        ParseResult parseResult = root.Parse(args);

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            Console.Error.WriteLine("Windows is the only supported OS.");
            return 1;
        }

        if (RequiresAdministrator(parseResult) && !adminChecker.IsAdministrator())
        {
            Console.Error.WriteLine("You must be running this application as an administrator.");
            return 1;
        }

        return await parseResult.InvokeAsync();
    }

    private static bool RequiresAdministrator(ParseResult parseResult)
    {
        for (
            Command command = parseResult.CommandResult.Command;
            command != null;
            command = command.Parents.OfType<Command>().FirstOrDefault()
        )
        {
            if (string.Equals(command.Name, "spectate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
