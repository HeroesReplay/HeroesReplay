namespace HeroesReplay.Core.Configuration
{
    public class ParseOptionsSettings
    {
        public bool ShouldParseEvents { get; set; }
        public bool ShouldParseMouseEvents { get; set; }
        public bool ShouldParseMessageEvents { get; set; }
        public bool ShouldParseStatistics { get; set; }
        public bool ShouldParseUnits { get; set; }
        public bool ShouldParseDetailedBattleLobby { get; set; }
    }
}