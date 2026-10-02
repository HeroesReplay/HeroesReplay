using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Core.HeroesData;

public interface IGameData
{
    IReadOnlyDictionary<string, UnitGroup> UnitGroups { get; }

    IReadOnlyList<Hero> Heroes { get; }

    IReadOnlyCollection<string> CoreUnits { get; }

    IReadOnlyCollection<string> BossUnits { get; }

    IReadOnlyCollection<string> VehicleUnits { get; }

    UnitGroup GetUnitGroup(string unitName);

    IReadOnlyList<Map> Maps { get; }

    Task LoadDataAsync();
}
