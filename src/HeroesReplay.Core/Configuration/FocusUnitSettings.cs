using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

public class FocusUnitSettings
{
    public IEnumerable<string> ObjectiveContains { get; set; } = new string[0];

    public IEnumerable<string> CoreContains { get; set; } = new string[0];

    public IEnumerable<string> CampContains { get; set; } = new string[0];

    public IEnumerable<string> PickupContains { get; set; } = new string[0];
}
