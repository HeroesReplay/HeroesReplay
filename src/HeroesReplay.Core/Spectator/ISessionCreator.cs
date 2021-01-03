using HeroesReplay.Core.Models;

namespace HeroesReplay.Core
{
    public interface ISessionCreator
    {
        void Create(StormReplay stormReplay);
    }
}