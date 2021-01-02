using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface ISessionCreator
    {
        void Create(StormReplay stormReplay);
    }
}