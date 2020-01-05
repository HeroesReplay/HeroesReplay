using System;
using System.Threading;

namespace HeroesReplay
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var consoleWaiter = new ManualResetEventSlim())
            {
                using (var provider = new ReplayProvider(@"G:\replays"))
                {
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        consoleWaiter.Set();
                    };

                    while (!consoleWaiter.IsSet)
                    {
                        if (provider.Unwatched.TryDequeue(out Game game))
                        {
                            using (var session = new GameController(game))
                            {
                                if (session.TryLaunchGameProcess())
                                {
                                    using (var waiter = new ManualResetEventSlim())
                                    {
                                        session.GameEnded += (sender, e) => OnTerminated(waiter, session); // when the game has ended

                                        session.Start(); // Start watching and spectating
                                        waiter.Wait(); // Wait until game replay has reached the end
                                        provider.Watched.Add(game); // Add to watched list
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Could not launch game process.");
                                }
                            }
                        }
                        else
                        {
                            provider.LoadAndParseReplays(count: 5);
                        }
                    }
                }
            }
        }

        private static void OnTerminated(ManualResetEventSlim waiter, GameController controller)
        {
            controller.TryKillGameProcess(); // Kill the game process
            waiter.Set(); // Unblock the waiter to continue
        }
    }
}