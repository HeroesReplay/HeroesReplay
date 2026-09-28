using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public sealed class NextGameSignal
{
    private readonly TaskCompletionSource<bool> signaled = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public bool IsSignaled => signaled.Task.IsCompleted;

    public void Signal()
    {
        signaled.TrySetResult(true);
    }

    public async Task DelayAsync(TimeSpan duration)
    {
        if (IsSignaled || duration <= TimeSpan.Zero)
        {
            return;
        }

        Task delay = Task.Delay(duration);
        await Task.WhenAny(delay, signaled.Task).ConfigureAwait(false);
    }
}
