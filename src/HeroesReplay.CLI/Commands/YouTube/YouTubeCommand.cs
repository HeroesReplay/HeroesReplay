using System.CommandLine;
using HeroesReplay.CLI.Commands.YouTube.Commands;

namespace HeroesReplay.CLI.Commands.YouTube;

public class YouTubeCommand : Command
{
    public YouTubeCommand()
        : base("youtube", "YouTube upload helpers for OBS recordings.")
    {
        Subcommands.Add(new UploaderCommand());
        Subcommands.Add(new LibraryCommand());
    }
}
