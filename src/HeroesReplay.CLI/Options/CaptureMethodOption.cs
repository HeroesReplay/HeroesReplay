using System.CommandLine;
using HeroesReplay.Core.Processes;

namespace HeroesReplay.CLI.Options
{
    public class CaptureMethodOption : Option
    {
        public CaptureMethodOption() : base(new[] { "--capture-method" }, description: "The window capture method.")
        {
            Required = false;
            Argument = new Argument<CaptureMethod>(() => CaptureMethod.BitBlt);
        }
    }
}