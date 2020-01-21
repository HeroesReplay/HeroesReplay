using System.CommandLine;
using System.CommandLine.Builder;
using System.IO;
using System.Linq;
using HeroesReplay.Shared;

namespace HeroesReplay.CLI.Options
{
    public class StormReplayFileOption : Option
    {
        public StormReplayFileOption() : base("--path", "The path to a single .StormReplay file.")
        {
            Required = !new DirectoryInfo(Constants.Heroes.DOCUMENTS_HEROES_REPLAYS_PATH).GetFiles(Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).Any();
            Argument = new Argument<FileInfo>(() => new DirectoryInfo(Constants.Heroes.DOCUMENTS_HEROES_REPLAYS_PATH).GetFiles().FirstOrDefault()) { Arity = ArgumentArity.ExactlyOne }.LegalFilePathsOnly();
        }
    }
}