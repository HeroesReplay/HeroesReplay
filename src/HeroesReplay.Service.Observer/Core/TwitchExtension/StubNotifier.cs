using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Analyzer;

namespace HeroesReplay.Service.Spectator.Core.HeroesProfileExtension
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