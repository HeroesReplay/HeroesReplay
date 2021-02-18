using System.CommandLine;

namespace HeroesReplay.CLI.Commands
{
    public class TwitchCommand : Command
    {
        public TwitchCommand() : base("twitch", $"")
        {
            AddCommand(new ConnectCommand());
        }
    }
}