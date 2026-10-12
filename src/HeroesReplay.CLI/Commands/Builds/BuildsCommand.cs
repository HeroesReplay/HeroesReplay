using System.CommandLine;
using HeroesReplay.CLI.Commands.Builds.Commands;

namespace HeroesReplay.CLI.Commands.Builds;

/// <summary>The talent-build reference (#412): import it from the guides, and match replays against it.</summary>
public class BuildsCommand : Command
{
    public BuildsCommand()
        : base(
            "builds",
            "The talent-build reference behind the YouTube talent-build lines: build names, talents and guide links."
        )
    {
        Subcommands.Add(new ImportCommand());
        Subcommands.Add(new MatchCommand());
    }
}
