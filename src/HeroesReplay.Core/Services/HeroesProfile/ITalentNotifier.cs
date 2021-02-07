
using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface ITalentNotifier
    {
        bool SessionCreated { get; }
        Task TryInitializeSessionAsync();
        Task SendCurrentTalentsAsync(TimeSpan timer);

        void ClearSession();
    }
}