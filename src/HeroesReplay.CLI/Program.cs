using System.CommandLine.Parsing;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI
{
    static class Program
    {
        public static async Task Main(string[] args)
        {
            using (ServiceProvider provider = new ServiceCollection()
                .AddSingleton<CommandLineService>()
                .AddSingleton<IAdminChecker, AdminChecker>()
                .BuildServiceProvider())
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    CommandLineService commandLineService = scope.ServiceProvider.GetRequiredService<CommandLineService>();

                    Parser parser = commandLineService.GetParser();

                    await parser.InvokeAsync(args);
                }
            }
        }
    }
}
