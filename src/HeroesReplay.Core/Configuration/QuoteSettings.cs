
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public class QuoteSettings
    {
        public IEnumerable<string> Subscriber { get; set; }
        public IEnumerable<string> Follower { get; set; }
    }
}