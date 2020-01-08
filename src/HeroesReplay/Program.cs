using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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
                .AddTransient<StateDetector>()
                .AddTransient<GameSpectator>()
                .AddTransient<ReplayService>()
                .AddTransient<GameProvider>()
                .AddTransient<GameController>()
                .AddTransient<GameWrapper>()
                .BuildServiceProvider();

            using(var scope = serviceProvider.CreateScope())
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