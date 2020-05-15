using System.CommandLine;

namespace HeroesReplay.CLI.Options
{
    public class MinimumReplayIdOption : Option
    {
        public MinimumReplayIdOption(int defaultReplayId) : base("--min-replay-id", description: "The replay id used as a starting point when accessing the HotsApi database.")
        {
            Required = false;
            Argument = new Argument<int>(getDefaultValue: () => defaultReplayId);
        }
    }
}