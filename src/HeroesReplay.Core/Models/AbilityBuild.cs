namespace HeroesReplay.Core.Models
{
    public record AbilityBuild
    {
        public int AbilityLink { get; init; }
        public int? GreaterEqualBuild { get; init; }
        public int? LessThanBuild { get; init; }
    }
}