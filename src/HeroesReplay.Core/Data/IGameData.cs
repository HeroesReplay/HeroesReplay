using HeroesReplay.Core.Services.HeroesProfile;
using System.Collections.Generic;
using System.Threading.Tasks;

using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Core.Runner
{
    public interface IGameData
    {
        IReadOnlyDictionary<string, UnitGroup> UnitGroups { get; }

        IReadOnlyList<Hero> Heroes { get; }

        IReadOnlyCollection<string> CoreNames { get; }

        UnitGroup GetUnitGroup(string unitName);

        IReadOnlyList<Map> Maps { get; }

        Task LoadDataAsync();
    }
}