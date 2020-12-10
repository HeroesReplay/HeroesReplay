namespace HeroesReplay.Core.Shared
{
    public record LocationSettings
    {
        public string BattlenetPath { get; init; }
        public string GameInstallPath { get; init; }
        public string ReplaySourcePath { get; init; }
    }
}