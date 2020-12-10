using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record UnitSettings
    {
        public IEnumerable<string> IgnoreNames { get; init; }
        public IEnumerable<string> CoreNames { get; init; }
        public IEnumerable<string> BossNames { get; init; }
        public IEnumerable<string> CampNames { get; init; }
        public IEnumerable<string> MapObjectiveNames { get; init; }
        public IEnumerable<string> CaptureNames { get; init; }
    }
}