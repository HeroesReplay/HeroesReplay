using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Context
{
    public interface IReplayContextSetter
    {
        Task SetContextAsync(LoadedReplay stormReplay);
    }
}