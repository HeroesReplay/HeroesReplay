using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI
{
    using Spectator;

    static class Program
    {
        public static async Task Main(string[] args)
        {
            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder.AddConsole())
                .AddSingleton<IConfiguration>(provider => new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddCommandLine(args).Build())
                .AddSingleton<CommandLineService>()
                .AddSingleton<CancellationTokenProvider>()
                .AddSingleton<RootReplayCommand>()
                .AddSingleton<HeroesOfTheStorm>()
                .AddSingleton<BattleNet>()
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormPlayerSelector>()
                .AddSingleton<Spectator>()
                .AddSingleton<StormReplayProvider>()
                .AddSingleton<StormReplayConsumer>()
                .AddSingleton<StormReplayRunner>()
                .AddSingleton<AdminChecker>()
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                CommandLineService commandLineService = scope.ServiceProvider.GetRequiredService<CommandLineService>();

                Parser parser = commandLineService.CreateCliParser();

                var result = await parser.InvokeAsync(args);
            }
        }
    }
}
