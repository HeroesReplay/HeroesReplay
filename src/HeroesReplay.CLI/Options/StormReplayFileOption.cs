using System.CommandLine;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class StormReplayFileOption : Option
    {
        public StormReplayFileOption() : base("--path", description: $"The path to a single {Constants.STORM_REPLAY_EXTENSION} file.")
        {
            Required = GetRequired();
            Argument = new Argument<FileInfo>(() => new DirectoryInfo(Constants.STORM_REPLAYS_USER_PATH).GetFiles().FirstOrDefault()) { Arity = ArgumentArity.ExactlyOne }.LegalFilePathsOnly();
        }

        private static bool GetRequired()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(Constants.STORM_REPLAYS_USER_PATH);

            if (directoryInfo.Exists)
            {
                return !directoryInfo.GetFiles(Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).Any();
            }

            return true;
        }
    }
}