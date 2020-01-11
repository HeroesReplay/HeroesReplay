using System;
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
            var serviceProvider = new ServiceCollection()
                .AddLogging(configuration => configuration.AddConsole())
                .AddSingleton<IConfiguration>(provider => new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build())
                .AddSingleton<Spectator>()
                .AddSingleton<ReplayConsumer>()
                .AddSingleton<StormReplayProvider>()
                .AddSingleton<GameRunner>()
                .AddSingleton<HeroesOfTheStorm>()
                .AddSingleton<BattleNet>()
                .AddSingleton<AdminChecker>()
                .AddSingleton<IStormReplayAnalyzer, DefaultStormReplayAnalyzer>()
                .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var adminChecker = scope.ServiceProvider.GetRequiredService<AdminChecker>(); 
                var replayConsumer = scope.ServiceProvider.GetRequiredService<ReplayConsumer>();

                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        cancellationTokenSource.Cancel();
                        logger.LogInformation("Service shutdown requested.");
                        logger.LogInformation("Please wait.");
                    };

                    if (adminChecker.IsAdministrator())
                    {
                        await replayConsumer.RunAsync(cancellationTokenSource.Token);
                    }
                    else
                    {
                        logger.LogCritical("This application must run as Administrator or screen capture will fail.");
                        Console.WriteLine("Press any key to exit.");
                        Console.ReadLine();
                    }
                }

                logger.LogInformation("Service shutdown.");
            }

            await Task.Delay(5000);
        }
    }
}