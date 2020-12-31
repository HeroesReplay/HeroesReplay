namespace HeroesReplay.Core.Shared
{
    public record ProcessSettings
    {
        public string? Battlenet { get; init; }
        public string? HeroesOfTheStorm { get; init; }
        public bool ForceLaunch { get; init; }
    }
}