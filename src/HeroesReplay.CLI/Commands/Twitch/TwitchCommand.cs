using System.CommandLine;
using HeroesReplay.CLI.Commands.Twitch.Commands;

namespace HeroesReplay.CLI.Commands.Twitch;

public class TwitchCommand : Command
{
    public TwitchCommand()
        : base("twitch", "Twitch bot, rewards, and chat integration.")
    {
        Subcommands.Add(new ConnectCommand());
        Subcommands.Add(new RewardsCommand());
    }
}
