using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record MapSettings
    {
        public IEnumerable<string> CarriedObjectives { get; init; }
        public IEnumerable<string> ARAM { get; init; }
    }
}