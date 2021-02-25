using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Configuration
{
    public class AbilityDetectionSettings
    {
        public AbilityDetection Taunt { get; set; }
        public AbilityDetection Dance { get; set; }
        public AbilityDetection Hearth { get; set; }
    }
}