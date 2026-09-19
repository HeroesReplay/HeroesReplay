using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Context;

public interface IReplayContextSetter
{
    Task SetContextAsync(LoadedReplay stormReplay);
}
