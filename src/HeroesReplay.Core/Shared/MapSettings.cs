using System.Collections.Generic;

namespace HeroesReplay.Core.Shared;

public class MapSettings
{
    public IEnumerable<string> CarriedObjectives { get; set; }
    public IEnumerable<MapDefinition> Catalog { get; set; }
}
