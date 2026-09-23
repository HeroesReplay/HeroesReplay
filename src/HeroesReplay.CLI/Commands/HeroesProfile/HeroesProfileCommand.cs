using System.CommandLine;
using HeroesReplay.CLI.Commands.HeroesProfile.Commands;

namespace HeroesReplay.CLI.Commands.HeroesProfile;

public class HeroesProfileCommand : Command
{
    public HeroesProfileCommand()
        : base("heroesprofile", "Heroes Profile downloader. Separate from the spectator process.")
    {
        Subcommands.Add(new DownloadCommand());
        Subcommands.Add(new PatchIndexCommand());
    }
}
