using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

public interface IGameManager
{
    Task<ReplaySessionKind> LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Func<Task<LoadedReplay>> whileReporting
    );
}
