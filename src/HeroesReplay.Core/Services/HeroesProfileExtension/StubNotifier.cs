
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class StubNotifier : ITalentNotifier
    {
        public void ClearSession()
        {

        }

        public Task SendCurrentTalentsAsync(TimeSpan timer, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }
    }
}