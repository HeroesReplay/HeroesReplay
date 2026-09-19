using System.CommandLine;
using HeroesReplay.CLI.Commands.Calculators;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Spectate;
using HeroesReplay.CLI.Commands.Twitch;
using HeroesReplay.CLI.Commands.YouTube;

namespace HeroesReplay.CLI.Commands;

public class HeroesReplayCommand : RootCommand
{
    public HeroesReplayCommand()
        : base("The HeroesReplay CLI")
    {
        Subcommands.Add(new SpectateCommand());
        Subcommands.Add(new CalculatorsCommand());
        Subcommands.Add(new CheckCommand());
        Subcommands.Add(new TwitchCommand());
        Subcommands.Add(new YouTubeCommand());
    }
}
