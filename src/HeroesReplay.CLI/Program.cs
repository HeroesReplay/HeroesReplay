using System.Threading.Tasks;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<CommandLineService>()
            .AddSingleton<IAdminChecker, AdminChecker>()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        CommandLineService commandLineService =
            scope.ServiceProvider.GetRequiredService<CommandLineService>();
        return await commandLineService.InvokeAsync(args);
    }
}
