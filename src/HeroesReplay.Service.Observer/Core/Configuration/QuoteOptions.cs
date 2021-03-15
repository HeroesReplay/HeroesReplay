
using System.Collections.Generic;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class QuoteOptions
    {
        public IEnumerable<string> Subscriber { get; set; }
        public IEnumerable<string> Follower { get; set; }
    }
}