using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Context;

public interface IContextFileManager
{
    Task WriteContextFilesAsync(ContextData contextData);
}
