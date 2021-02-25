using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class AbilityDetection
    {
        public int? CmdIndex { get; set; }
        public IEnumerable<AbilityBuild> AbilityBuilds { get; set; }
    }
}