using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .AddLogging(configuration => configuration.AddConsole())
                .AddSingleton<StateDetector>()
                .AddSingleton<GameSpectator>()
                .AddSingleton<ReplayService>()
                .AddSingleton<GameProvider>()
                .AddSingleton<GameController>()
                .AddSingleton<GameWrapper>()
                .AddSingleton<IConfiguration>((provider) => new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build())
                .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ReplayService>();

                using (var cancellationTokenSource = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        cancellationTokenSource.Cancel();
                        Console.WriteLine("Service shutdown requested.");
                        Console.WriteLine("Please wait.");
                    };

                    await service.RunAsync(cancellationTokenSource.Token);
                }
            }

            Console.WriteLine("Service shutdown.");
            await Task.Delay(5000);
        }
    }
}