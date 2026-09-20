using System.CommandLine;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class PredictionsCommand : Command
{
    public PredictionsCommand()
        : base("predictions", "Helix Blue/Red match predictions")
    {
        Subcommands.Add(new PredictionsTestCommand());
    }
}
