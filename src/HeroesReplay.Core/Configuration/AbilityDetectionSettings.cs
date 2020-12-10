namespace HeroesReplay.Core.Shared
{
    public record AbilityDetectionSettings
    {
        public AbilityDetection Taunt { get; init; }
        public AbilityDetection Dance { get; init; }
        public AbilityDetection Hearth { get; init; }
    }
}