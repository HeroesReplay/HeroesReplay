using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay
{
    public class ConsoleService
    {
        public const string ARGUMENT_REPLAY_PATH = "replay";
        public const string ARGUMENT_LAUNCH_GAME = "launch";
        public const string ARGUMENT_BATTLE_NET = "bnet";
        public const string ARGUMENT_REPLAY_DIRECTORY = "replays";

        private readonly IConfiguration configuration;
        private readonly AdminChecker adminChecker;
        private readonly CancellationTokenSource source;
        private readonly HeroesOfTheStorm heroesOfTheStorm;

        public ConsoleService(IConfiguration configuration, AdminChecker adminChecker, CancellationTokenSource source, HeroesOfTheStorm heroesOfTheStorm)
        {
            this.configuration = configuration;
            this.adminChecker = adminChecker;
            this.source = source;
            this.heroesOfTheStorm = heroesOfTheStorm;
        }

        public void WriteHello()
        {
            Console.WriteLine("==================================");
            Console.WriteLine("     Heroes Replay Starting       ");
            Console.WriteLine("    Press Ctrl+C to shutdown      ");
            Console.WriteLine("==================================");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                source.Cancel();

                Console.WriteLine("=======================================================");
                Console.WriteLine("           Service shutdown requested                  ");
                Console.WriteLine("Please wait for the application to gracefully shutdown.");
                Console.WriteLine("=======================================================");
            };
        }

        public bool HasReplayArgument => !string.IsNullOrWhiteSpace(configuration.GetValue<string>(ARGUMENT_REPLAY_PATH));
        public bool HasReplayDirectoryArgument => !string.IsNullOrWhiteSpace(configuration.GetValue<string>(ARGUMENT_REPLAY_DIRECTORY));
        public bool HasLaunchArgument => configuration.GetValue<bool>(ARGUMENT_LAUNCH_GAME);
        public bool HasBattleNetArgument => !string.IsNullOrWhiteSpace(configuration.GetValue<string>(ARGUMENT_BATTLE_NET));

        public string BattleNetPath => configuration.GetValue<string>(ARGUMENT_BATTLE_NET);
        public string ReplayFile => configuration.GetValue<string>(ARGUMENT_REPLAY_PATH);
        public string ReplaysDirectory => configuration.GetValue<string>(ARGUMENT_REPLAY_DIRECTORY);
        public bool LaunchGame => configuration.GetValue<bool>(ARGUMENT_LAUNCH_GAME, true);

        public bool IsValid()
        {
            if (!adminChecker.IsAdministrator())
            {
                Console.WriteLine("=======================================================================");
                Console.WriteLine("This application must run as Administrator or screen capture will fail.");
                Console.WriteLine("                    Press any key to exit.                             ");
                Console.WriteLine("=======================================================================");
                Console.ReadLine();

                Environment.ExitCode = (int)ExitCode.ERROR_NO_ADMIN_ACCESS;

                return false;
            }

            if (!HasBattleNetArgument || (HasBattleNetArgument && !Directory.Exists(BattleNetPath)))
            {
                Console.WriteLine("=========================================================================");
                Console.WriteLine("      Please provide the path to the Battle.net installation folder.     ");
                Console.WriteLine("                     Press any key to exit.                              ");
                Console.WriteLine("=========================================================================");
                Console.ReadLine();

                Environment.ExitCode = (int)ExitCode.ERROR_BATTLE_NET_NOT_DETECTED;

                return false;
            }

            if (HasReplayArgument && !File.Exists(ReplayFile))
            {
                Console.WriteLine("=========================================================================");
                Console.WriteLine(" The provided replay file could not be found. Please enter a valid path. ");
                Console.WriteLine("                     Press any key to exit.                              ");
                Console.WriteLine("=========================================================================");
                Console.ReadLine();

                Environment.ExitCode = (int)ExitCode.ERROR_INVALID_REPLAY_FILE;

                return false;
            }

            if (HasLaunchArgument && !heroesOfTheStorm.IsRunning)
            {
                Console.WriteLine($"=========================================================================");
                Console.WriteLine($"            The the game process could not be found.                     ");
                Console.WriteLine($"                     Press any key to exit.                              ");
                Console.WriteLine($"=========================================================================");
                Console.ReadLine();

                Environment.ExitCode = (int)ExitCode.ERROR_GAME_NOT_DETECTED;

                return false;
            }

            if ((HasReplayDirectoryArgument && !Directory.Exists(ReplaysDirectory)) || (HasReplayDirectoryArgument && !Directory.EnumerateFiles(ReplaysDirectory, "*.StormReplay", SearchOption.AllDirectories).Any()))
            {
                Console.WriteLine($"=========================================================================");
                Console.WriteLine($"  The directory provided for replays does not exist or was found empty.  ");
                Console.WriteLine($"                     Press any key to exit.                              ");
                Console.WriteLine($"=========================================================================");
                Console.ReadLine();

                Environment.ExitCode = (int)ExitCode.ERROR_REPLAYS_DIRECTORY_INVALID;

                return false;
            }

            return true;
        }

        public void WriteGoodbye()
        {
            Console.WriteLine("================");
            Console.WriteLine("Service shutdown");
            Console.WriteLine("================");
        }

    }
}