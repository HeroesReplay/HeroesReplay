using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder.AddConsole())
                .AddSingleton<IConfiguration>(provider => new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddCommandLine(args).Build())
                .AddSingleton<HeroesOfTheStorm>()
                .AddSingleton<BattleNet>()
                .AddSingleton<IStormReplayAnalyzer, DefaultStormReplayAnalyzer>()
                .AddSingleton<Spectator>()
                .AddSingleton<StormReplayProvider>()
                .AddSingleton<ReplayConsumer>()
                .AddSingleton<GameRunner>()
                .AddSingleton<AdminChecker>()
                .AddSingleton<ConsoleService>()
                .AddSingleton<CancellationTokenSource>()
                .AddSingleton<EntryPoint>()
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                var entryPoint = scope.ServiceProvider.GetRequiredService<EntryPoint>();
                await entryPoint.RunHeroesReplayAsync();
            }
        }
    }
}