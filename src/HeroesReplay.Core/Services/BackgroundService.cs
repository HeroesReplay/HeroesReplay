using Microsoft.Extensions.Hosting;

using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services
{
    public abstract class BackgroundService : IHostedService
    {
        private readonly CancellationTokenSource _stoppingCts;
        private Task _executingTask;

        public BackgroundService(CancellationTokenSource _stoppingCts)
        {
            this._stoppingCts = _stoppingCts;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _executingTask = ExecuteAsync(_stoppingCts.Token);

            // If the task is completed then return it, this will bubble cancellation and failure to the caller
            if (_executingTask.IsCompleted)
            {
                return _executingTask;
            }

            // Otherwise it's running
            return Task.CompletedTask;
        }

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // Stop called without start
            if (_executingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                _stoppingCts.Cancel();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
    }
}
