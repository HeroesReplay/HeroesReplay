
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public record QuoteSettings
    {
        public IEnumerable<string> Subscriber { get; init; }
        public IEnumerable<string> Follower { get; init; }
    }
}