using System;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException) { }

        // services start names the role it launches. Its console takes the role's title.
        ServiceConsoleTitle.Apply(
            Environment.GetEnvironmentVariable(ServiceReadyFile.RoleVariable)
        );
        return await new CommandLineService().InvokeAsync(args);
    }
}
