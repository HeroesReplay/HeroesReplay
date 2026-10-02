using System;
using System.IO;
using System.Threading.Tasks;

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

        return await new CommandLineService().InvokeAsync(args);
    }
}
