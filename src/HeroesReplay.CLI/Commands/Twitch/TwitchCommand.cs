using System.CommandLine;
using HeroesReplay.CLI.Commands.Twitch.Commands;

namespace HeroesReplay.CLI.Commands.Twitch
{
    public class TwitchCommand : Command
    {
        public TwitchCommand() : base("twitch", $"")
        {
            AddCommand(new ConnectCommand());
            AddCommand(new RewardsCommand());
        }
    }
}