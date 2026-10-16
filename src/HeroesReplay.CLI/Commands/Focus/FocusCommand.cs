using System.CommandLine;
using HeroesReplay.CLI.Commands.Focus.Commands;

namespace HeroesReplay.CLI.Commands.Focus;

/// <summary>The cycle's own focus-hero replays (#433): rank candidates, and report coverage.</summary>
public class FocusCommand : Command
{
    public FocusCommand()
        : base(
            "focus",
            "Focus-hero replays: which player of a replay is worth a whole match on one hero, and which heroes, roles, builds, maps, leagues and regions the focus picks have covered."
        )
    {
        Subcommands.Add(new CandidatesCommand());
        Subcommands.Add(new CoverageCommand());
    }
}
