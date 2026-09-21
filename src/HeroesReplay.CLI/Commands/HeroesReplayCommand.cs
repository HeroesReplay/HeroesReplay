using System.CommandLine;
using HeroesReplay.CLI.Commands.Calculators;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.CLI.Commands.Client;
using HeroesReplay.CLI.Commands.HeroesProfile;
using HeroesReplay.CLI.Commands.Otel;
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
        Subcommands.Add(new ClientCommand());
        Subcommands.Add(new OtelCommand());
        Subcommands.Add(new McpCommand());
        Subcommands.Add(new TwitchCommand());
        Subcommands.Add(new YouTubeCommand());
        Subcommands.Add(new HeroesProfileCommand());
    }
}
