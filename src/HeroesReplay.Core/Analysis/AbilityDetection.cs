using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis;

public class AbilityDetection
{
    public int? CmdIndex { get; set; }
    public IEnumerable<AbilityBuild> AbilityBuilds { get; set; }
}
