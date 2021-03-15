using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Context
{
    public interface IContextManager
    {
        Task WriteContextFilesAsync();
        Task SetContextAsync(LoadedReplay stormReplay);
    }
}