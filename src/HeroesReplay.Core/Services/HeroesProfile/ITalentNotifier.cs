
using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface ITalentNotifier
    {
        Task SendCurrentTalentsAsync(TimeSpan timer);
        void ClearSession();
    }
}