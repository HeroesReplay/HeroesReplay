using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public interface ITalentNotifier
    {
        Task SendCurrentTalentsAsync(TimeSpan timer, CancellationToken token = default);
        void ClearSession();
    }
}