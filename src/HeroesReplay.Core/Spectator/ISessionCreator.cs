using HeroesReplay.Core.Shared;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface ISessionCreator
    {
        Task CreateAsync(StormReplay stormReplay);
    }
}