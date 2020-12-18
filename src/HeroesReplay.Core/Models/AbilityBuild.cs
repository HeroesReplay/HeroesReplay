namespace HeroesReplay.Core.Shared
{
    public record AbilityBuild
    {
        public int AbilityLink { get; init; }
        public int? GreaterEqualBuild { get; init; }
        public int? LessThanBuild { get; init; }
    }
}