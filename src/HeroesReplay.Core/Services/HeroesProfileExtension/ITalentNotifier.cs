using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public interface ITalentNotifier
{
    Task SendCurrentTalentsAsync(TimeSpan timer, bool clockLive, CancellationToken token = default);

    Task EndGameAsync(CancellationToken token = default);

    void ClearSession();
}
