using System.CommandLine;
using HeroesReplay.CLI.Commands.Spectate.Commands;

namespace HeroesReplay.CLI.Commands.Spectate
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