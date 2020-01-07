using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using(var cancellationTokenSource = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) => 
                {
                    e.Cancel = true;
                    cancellationTokenSource.Cancel();
                    Console.WriteLine("Service shutdown requested.");
                    Console.WriteLine("Please wait.");
                };

                using(var spectator = new GameSpectator(new StateDetector()))
                {
                    using(var controller = new GameController(spectator))
                    {
                        var service = new ReplayService(new GameProvider("G:\\replays\\input", "G:\\replays\\finished", "G:\\replays\\invalid"), controller);
                        
                        await service.RunAsync(cancellationTokenSource.Token);

                        Win32.TryKillGame();
                    }
                }
            }

            Console.WriteLine("Service shutdown.");
            await Task.Delay(5000);
        }
    }
}