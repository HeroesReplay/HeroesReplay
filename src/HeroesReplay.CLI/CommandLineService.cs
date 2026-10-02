using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands;

namespace HeroesReplay.CLI;

/// <summary>
/// No command needs administrator rights except <c>client firewall</c>, which checks for itself (#133).
/// </summary>
public class CommandLineService
{
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

        return await parseResult.InvokeAsync(configuration);
    }
}
