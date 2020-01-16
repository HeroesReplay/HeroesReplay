using System.CommandLine;
using System.CommandLine.Builder;
using System.IO;
using System.Linq;
using HeroesReplay.Spectator;
using Process = System.Diagnostics.Process;

namespace HeroesReplay.CLI
{
    public class RootReplayCommand : RootCommand
    {
        public RootReplayCommand() : base(description: "The Heroes of the Storm Automated Replay Spectator.")
        {
            AddOption(new PathOption());
            AddOption(new LaunchOption());
            AddOption(new BnetOption());
        }

        class PathOption : Option
        {
            public PathOption() : base("--path", "The path to a single .StormReplay file or a directory containing .StormReplay files.")
            {
                Required = !new DirectoryInfo(Constants.Heroes.DOCUMENTS_HEROES_REPLAYS_PATH).GetFiles(Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).Any();
                Argument = new Argument<string>(() => Constants.Heroes.DOCUMENTS_HEROES_REPLAYS_PATH) { Arity = ArgumentArity.ZeroOrOne }.LegalFilePathsOnly();
            }
        }

        class LaunchOption : Option
        {
            public LaunchOption() : base(new[] { "--launch", "-l" }, "Launch the game or spectate the existing process.")
            {
                Required = false;
                Argument = new Argument<bool>(() => !Process.GetProcessesByName(Constants.Heroes.HEROES_PROCESS_NAME).Any());
            }
        }

        class BnetOption : Option
        {
            public BnetOption() : base(new[] { "--bnet" }, "The directory that contains Battle.net")
            {
                Required = !Directory.Exists(Constants.Bnet.BATTLE_NET_DEFAULT_INSTALL_PATH);
                Argument = new Argument<DirectoryInfo>(() => new DirectoryInfo(Constants.Bnet.BATTLE_NET_DEFAULT_INSTALL_PATH)).ExistingOnly();
            }
        }
    }
}