using System.CommandLine;
using HeroesReplay.CLI.Commands.Calculators;
using HeroesReplay.CLI.Commands.Spectate;
using HeroesReplay.CLI.Commands.Twitch;

namespace HeroesReplay.CLI.Commands
{
    public class HeroesReplayCommand : RootCommand
    {
        public HeroesReplayCommand() : base("HeroesReplay: The Heroes of the Storm automated spectator.")
        {
            AddCommand(new SpectateCommand());
            AddCommand(new CalculatorsCommand());
            AddCommand(new TwitchCommand());
        }
    }
}