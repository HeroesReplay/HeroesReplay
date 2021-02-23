using System.CommandLine;

namespace HeroesReplay.CLI.Commands
{
    public class YouTubeCommand : Command
    {
        public YouTubeCommand() : base("youtube", $"")
        {
            AddCommand(new UploaderCommand());
        }
    }
}