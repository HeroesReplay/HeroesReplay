using System.CommandLine;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class RewardsCommand : Command
{
    public RewardsCommand()
        : base("rewards", "Creates, updates, lists, or removes custom rewards for the channel")
    {
        Subcommands.Add(new GenerateCommand());
        Subcommands.Add(new SubmitCommand());
        Subcommands.Add(new ListCommand());
        Subcommands.Add(new RemoveUnrankedDraftCommand());
        Subcommands.Add(new TestCommand());
    }
}
