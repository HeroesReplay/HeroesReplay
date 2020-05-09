using System.CommandLine;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class StormReplayDirectoryOption : Option
    {
        public StormReplayDirectoryOption(string? defaultPath = null) : base("--source-path", description: $"The path to a directory for {Constants.STORM_REPLAY_EXTENSION} files.")
        {
            Required = GetRequired(defaultPath);
            Argument = new Argument<DirectoryInfo>(getDefaultValue: () => new DirectoryInfo(defaultPath)) { Arity = ArgumentArity.ZeroOrOne }.LegalFilePathsOnly();
        }

        private static bool GetRequired(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            DirectoryInfo directoryInfo = new DirectoryInfo(path);

            if (directoryInfo.Exists)
            {
                return !directoryInfo.GetFiles(Constants.STORM_REPLAY_WILDCARD, SearchOption.AllDirectories).Any();
            }

            return true;
        }
    }
}