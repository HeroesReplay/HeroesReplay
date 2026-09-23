using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public class StubNotifier : ITalentNotifier
{
    public void ClearSession() { }

    public Task SendCurrentTalentsAsync(
        TimeSpan timer,
        bool clockLive,
        CancellationToken token = default
    )
    {
        return Task.CompletedTask;
    }

    public Task EndGameAsync(CancellationToken token = default)
    {
        return Task.CompletedTask;
    }
}
