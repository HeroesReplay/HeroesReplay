using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI;

public class CommandLineService
{
    private readonly IAdminChecker adminChecker;

    public CommandLineService(IAdminChecker adminChecker)
    {
        this.adminChecker = adminChecker;
    }

    public Task<int> InvokeAsync(string[] args) => InvokeAsync(args, null);

    public async Task<int> InvokeAsync(string[] args, InvocationConfiguration configuration)
    {
        var root = new HeroesReplayCommand();
        ParseResult parseResult = root.Parse(args);
        TextWriter error = configuration?.Error ?? Console.Error;

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            error.WriteLine("Windows is the only supported OS.");
            return 1;
        }

        if (RequiresAdministrator(parseResult) && !adminChecker.IsAdministrator())
        {
            error.WriteLine("You must be running this application as an administrator.");
            return 1;
        }

        return await parseResult.InvokeAsync(configuration);
    }

    /// <summary>
    /// True only when a <c>spectate</c> command will run its own action. Help, version,
    /// completion directives, and parse errors do not run it, so they do not need elevation.
    /// </summary>
    public static bool RequiresAdministrator(ParseResult parseResult)
    {
        Command target = parseResult.CommandResult.Command;
        if (
            parseResult.Errors.Count > 0
            || target.Action == null
            || !ReferenceEquals(parseResult.Action, target.Action)
        )
        {
            return false;
        }

        for (
            Command command = target;
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
