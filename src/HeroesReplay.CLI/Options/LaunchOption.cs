using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using HeroesReplay.Shared;

namespace HeroesReplay.CLI.Options
{
    public class LaunchOption : Option
    {
        public LaunchOption() : base(new[] { "--launch" }, "Launch the game or use the existing game process. Setting this to false can help during development.")
        {
            Required = false;
            Argument = new Argument<bool>(() => !Process.GetProcessesByName(Constants.Heroes.HEROES_PROCESS_NAME).Any());
        }
    }
}