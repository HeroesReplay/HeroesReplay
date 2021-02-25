using System.CommandLine;
using HeroesReplay.CLI.Commands.Calculators.Commands;

namespace HeroesReplay.CLI.Commands.Calculators
{
    public class CalculatorsCommand : Command
    {
        public CalculatorsCommand() : base("calculators", $"Analyze .StormReplay files using built-in replay analysis.")
        {
            AddCommand(new ReportCommand());
        }
    }
}
