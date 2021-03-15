using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analyzer;

namespace HeroesReplay.Core.Configuration
{
    public class AbilityDetectionOptions
    {
        public AbilityDetection Taunt { get; set; }
        public AbilityDetection Dance { get; set; }
        public AbilityDetection Hearth { get; set; }
    }
}