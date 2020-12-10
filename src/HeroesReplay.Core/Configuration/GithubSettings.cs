namespace HeroesReplay.Core.Shared
{
    public record GithubSettings
    {
        public string User { get; init; }
        public string AccessToken { get; init; }
    }
}