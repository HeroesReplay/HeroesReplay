using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record AbilityDetection
    {
        public int? CmdIndex { get; init; }
        public IEnumerable<AbilityBuild> AbilityBuilds { get; init; }
    }
}