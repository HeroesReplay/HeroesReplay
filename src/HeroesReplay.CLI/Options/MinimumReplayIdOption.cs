using System.CommandLine;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class MinimumReplayIdOption : Option
    {
        public MinimumReplayIdOption(int defaultReplayId) : base("--min-replay-id", description: "The minimum replay id used as a starting point when accessing the HotsApi database. Please refer to the HotsApi documentation. Old replays will require old game clients and assets to be downloaded.")
        {
            Required = false;
            Argument = new Argument<int>(getDefaultValue: () => defaultReplayId);
        }
    }
}