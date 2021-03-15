using HeroesReplay.Core.Services.Analyzer;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class AbilityDetectionOptions
    {
        public AbilityDetection Taunt { get; set; }
        public AbilityDetection Dance { get; set; }
        public AbilityDetection Hearth { get; set; }
    }
}