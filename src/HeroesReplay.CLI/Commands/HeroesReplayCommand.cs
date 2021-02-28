using System.CommandLine;

using HeroesReplay.CLI.Commands.Calculators;
using HeroesReplay.CLI.Commands.Spectate;
using HeroesReplay.CLI.Commands.Twitch;
using HeroesReplay.CLI.Commands.YouTube;

namespace HeroesReplay.CLI.Commands
{
    public class HeroesReplayCommand : RootCommand
    {
        public HeroesReplayCommand() : base("The HeroesReplay CLI")
        {
            AddCommand(new SpectateCommand());
            AddCommand(new CalculatorsCommand());
            AddCommand(new TwitchCommand());
            AddCommand(new YouTubeCommand());
        }
    }
}