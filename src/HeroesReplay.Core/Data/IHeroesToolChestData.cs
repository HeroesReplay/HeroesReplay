using Heroes.ReplayParser;

using System.Threading.Tasks;

using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Core.Runner
{
    public interface IHeroesToolChestData
    {
        UnitGroup GetUnitGroup(Unit unit);

        Task LoadDataAsync();
    }
}