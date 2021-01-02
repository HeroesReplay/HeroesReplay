using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Configuration
{
    public record AbilityDetectionSettings
    {
        public AbilityDetection Taunt { get; init; }
        public AbilityDetection Dance { get; init; }
        public AbilityDetection Hearth { get; init; }
    }
}