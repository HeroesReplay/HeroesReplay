using System.CommandLine;
using System.IO;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class StormReplayFileOption : Option
    {
        public StormReplayFileOption() : base("--path", description: $"The path to a single {Constants.STORM_REPLAY_EXTENSION} file.")
        {
            Required = true;
            Argument = new Argument<FileInfo>() { Arity = ArgumentArity.ExactlyOne }.LegalFilePathsOnly();
        }
    }
}