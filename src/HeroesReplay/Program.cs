using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HeroesReplay
{
    class Program
    {
        static void Main(string[] args)
        {
            Win32.TryKillGame();

            using (var consoleWaiter = new ManualResetEventSlim())
            {
                using (var provider = new ReplayProvider("G:\\replays"))
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
                                        session.StateChanged += (sender, e) =>
                                        {
                                            if (e.Data.Current == GameState.EndOfGame)
                                            {
                                                session.TryKillGameProcess();
                                                waiter.Set();
                                            }
                                        };

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
    }
}