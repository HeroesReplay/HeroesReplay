using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public record MapSettings
    {
        public IEnumerable<string> CarriedObjectives { get; init; }
        public IEnumerable<string> ARAM { get; init; }
    }
}