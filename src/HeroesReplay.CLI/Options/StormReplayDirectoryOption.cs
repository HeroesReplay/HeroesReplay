using System.CommandLine;
using System.CommandLine.Builder;
using System.IO;
using System.Linq;
using HeroesReplay.Shared;

namespace HeroesReplay.CLI.Options
{
    public class StormReplayDirectoryOption : Option
    {
        public StormReplayDirectoryOption() : base("--path", "The path to a directory of .StormReplay files.")
        {
            Required = !new DirectoryInfo(Constants.STORM_REPLAYS_USER_PATH).GetFiles(Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).Any();
            Argument = new Argument<DirectoryInfo>(() => new DirectoryInfo(Constants.STORM_REPLAYS_USER_PATH)) { Arity = ArgumentArity.ZeroOrOne }.LegalFilePathsOnly();
        }
    }
}