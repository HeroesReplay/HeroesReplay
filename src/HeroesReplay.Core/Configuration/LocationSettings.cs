namespace HeroesReplay.Core.Configuration
{
    public record LocationSettings
    {
        public string BattlenetPath { get; init; }
        public string ReplaySource { get; init; }
        public string GameInstallDirectory { get; init; }
    }
}