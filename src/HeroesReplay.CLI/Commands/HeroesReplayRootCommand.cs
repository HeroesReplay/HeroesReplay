using System.CommandLine;

namespace HeroesReplay.CLI.Commands
{
    public class HeroesReplayRootCommand : RootCommand
    {
        public HeroesReplayRootCommand() : base("HeroesReplay: The Heroes of the Storm automated spectator.")
        {
            AddCommand(new SpectateCommand());
            AddCommand(new AnalyzerCommand());
        }
    }
}