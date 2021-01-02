namespace HeroesReplay.Core.Configuration
{
    public record ProcessSettings
    {
        public string? Battlenet { get; init; }
        public string? HeroesOfTheStorm { get; init; }
        public bool ForceLaunch { get; init; }
    }
}