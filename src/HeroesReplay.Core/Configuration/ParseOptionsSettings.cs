namespace HeroesReplay.Core.Shared
{
    public record ParseOptionsSettings
    {
        public bool ShouldParseEvents { get; init; }
        public bool ShouldParseMouseEvents { get; init; }
        public bool ShouldParseMessageEvents { get; init; }
        public bool ShouldParseStatistics { get; init; }
        public bool ShouldParseUnits { get; init; }
        public bool ShouldParseDetailedBattleLobby { get; init; }
    }
}