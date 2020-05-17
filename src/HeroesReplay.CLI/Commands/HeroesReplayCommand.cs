using System.CommandLine;

namespace HeroesReplay.CLI.Commands
{
    public class HeroesReplayCommand : RootCommand
    {
        public HeroesReplayCommand() : base("HeroesReplay: The Heroes of the Storm automated spectator.")
        {
            AddCommand(new SpectateCommand());
        }
    }
}