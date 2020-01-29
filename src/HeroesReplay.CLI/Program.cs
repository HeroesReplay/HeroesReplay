using System.CommandLine.Parsing;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI
{
    static class Program
    {
        public static async Task Main(string[] args)
        {
            ServiceProvider provider = new ServiceCollection()
                .AddSingleton<CommandLineService>()
                .AddSingleton<AdminChecker>()
                .BuildServiceProvider();

            using (IServiceScope scope = provider.CreateScope())
            {
                CommandLineService commandLineService = scope.ServiceProvider.GetRequiredService<CommandLineService>();

                Parser parser = commandLineService.GetParser();

                await parser.InvokeAsync(args);
            }
        }
    }
}
