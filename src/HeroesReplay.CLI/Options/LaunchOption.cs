using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Options
{
    public class LaunchOption : Option
    {
        public LaunchOption() : base("--launch", description: $"Launch the game or use the existing {Constants.HEROES_PROCESS_NAME}.exe process. Setting this to 'false' can help during development.")
        {
            Required = false;
            Argument = new Argument<bool>(getDefaultValue: () => !Process.GetProcessesByName(Constants.HEROES_PROCESS_NAME).Any());
        }
    }
}