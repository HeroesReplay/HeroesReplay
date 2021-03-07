using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Context
{
    public interface IContextManager
    {
        Task WriteContextFilesAsync();
        Task SetContextAsync(LoadedReplay stormReplay);
    }
}