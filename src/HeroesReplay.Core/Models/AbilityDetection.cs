using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public record AbilityDetection
    {
        public int? CmdIndex { get; init; }
        public IEnumerable<AbilityBuild> AbilityBuilds { get; init; }
    }
}