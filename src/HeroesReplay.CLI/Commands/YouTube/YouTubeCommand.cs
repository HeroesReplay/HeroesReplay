using System.CommandLine;
using HeroesReplay.CLI.Commands.YouTube.Commands;

namespace HeroesReplay.CLI.Commands.YouTube
{
    public class YouTubeCommand : Command
    {
        public YouTubeCommand() : base("youtube", $"")
        {
            AddCommand(new UploaderCommand());
        }
    }
}