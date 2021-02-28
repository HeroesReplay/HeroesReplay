using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Context
{
    public interface IContextFileManager
    {
        Task WriteContextFilesAsync(ContextData contextData);
    }
}