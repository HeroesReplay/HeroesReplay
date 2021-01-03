using System.CommandLine;

namespace HeroesReplay.CLI.Commands.Analyze
{
    public class AnalyzeCommand : Command
    {
        public AnalyzeCommand() : base("analyze", $"Analyze .StormReplay files using built-in replay analysis.")
        {
            AddCommand(new AnalyzeFileCommand());
        }
    }
}
