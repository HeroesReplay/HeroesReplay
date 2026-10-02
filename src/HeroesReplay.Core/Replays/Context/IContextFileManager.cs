using System.Threading.Tasks;

namespace HeroesReplay.Core.Replays.Context;

public interface IContextFileManager
{
    Task WriteContextFilesAsync(ContextData contextData);
}
