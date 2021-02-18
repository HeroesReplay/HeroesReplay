using System.CommandLine;

namespace HeroesReplay.CLI.Commands.Analyze
{
    public class CalculatorsCommand : Command
    {
        public CalculatorsCommand() : base("calculators", $"Analyze .StormReplay files using built-in replay analysis.")
        {
            AddCommand(new ReportCommand());
        }
    }
}
