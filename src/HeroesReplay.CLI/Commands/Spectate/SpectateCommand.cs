using System.CommandLine;

namespace HeroesReplay.CLI.Commands
{
    public class SpectateCommand : Command
    {
        public SpectateCommand() : base("spectate", $"Auto Spectate .StormReplay files using built-in replay analysis which auto focuses on heroes based on events that happen.")
        {
            AddCommand(new SpectateFileCommand());
            AddCommand(new SpectateHeroesProfileApiCommand());
        }
    }
}