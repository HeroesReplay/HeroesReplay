namespace HeroesReplay.Core.Configuration
{
    public record LocationSettings
    {
        public string BattlenetPath { get; init; }
        public string GameInstallPath { get; init; }
        public string ReplaySourcePath { get; init; }
    }
}