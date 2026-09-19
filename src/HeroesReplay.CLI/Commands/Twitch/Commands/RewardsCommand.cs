using System.CommandLine;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class RewardsCommand : Command
{
    public RewardsCommand()
        : base("rewards", "Creates or updates the custom rewards for the channel")
    {
        Subcommands.Add(new GenerateCommand());
        Subcommands.Add(new SubmitCommand());
    }
}
