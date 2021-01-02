namespace HeroesReplay.Core.Configuration
{
    public record GithubSettings
    {
        public string User { get; init; }
        public string AccessToken { get; init; }
    }
}