using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

/// <summary>
/// #166: replay 65625279 was a viewer's ReplayId request and still got a Blue/Red prediction.
/// The session status must carry the request so twitch connect opens nothing.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RequestedReplayPredictionTests : IDisposable
{
    private const int ReplayId = 65625279;

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-request-prediction-" + Guid.NewGuid().ToString("N")
    );

    public RequestedReplayPredictionTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShowLoading_WritesSuppressPredictionsForARequestedReplay()
    {
        var requested = new SpectatorStatus();
        var ordinary = new SpectatorStatus { SuppressPredictions = true };

        GameManager.ShowLoading(requested, Requested(), current: null);
        GameManager.ShowLoading(ordinary, new LoadedReplay { ReplayId = ReplayId }, null);

        Assert.True(requested.SuppressPredictions);
        Assert.Equal(ReplayId, requested.ReplayId);
        Assert.Equal("Loading", requested.Phase);
        Assert.Equal("Industrial District", requested.Map);
        Assert.False(ordinary.SuppressPredictions);
    }

    [Fact]
    public async Task Watcher_OpensNoPredictionForARequestedSession()
    {
        Opens predictions = await RunToTimer(Requested());

        Assert.Equal(0, predictions.Count);
    }

    [Fact]
    public async Task Watcher_StillOpensAPredictionForAnOrdinarySession()
    {
        Opens predictions = await RunToTimer(
            new LoadedReplay
            {
                ReplayId = ReplayId,
                HeroesProfileReplay = new HeroesProfileReplay { Map = "Industrial District" },
            }
        );

        Assert.Equal(1, predictions.Count);
    }

    private async Task<Opens> RunToTimer(LoadedReplay loaded)
    {
        var store = new SpectatorStatusStore(Path.Combine(directory, "status.json"));
        store.Patch(status => GameManager.ShowLoading(status, loaded, current: null));
        store.Patch(status =>
        {
            status.Phase = "TimerDetected";
            status.Timer = "00:00:05";
            status.SnapshotStale = false;
        });

        var predictions = new Opens();
        var watcher = new StatusPredictionWatcher(
            NullLogger<StatusPredictionWatcher>.Instance,
            new AppSettings { Twitch = new TwitchSettings { EnableChatBot = false } },
            store,
            predictions,
            SilentTwitch.Create()
        )
        {
            ReplaySessionFilePath = Path.Combine(directory, "replay-sessions.txt"),
        };
        var tracker = new PredictionSessionTracker();

        await watcher.StepAsync(tracker, CancellationToken.None);
        await watcher.StepAsync(tracker, CancellationToken.None);
        return predictions;
    }

    private static LoadedReplay Requested() =>
        new()
        {
            ReplayId = ReplayId,
            HeroesProfileReplay = new HeroesProfileReplay { Map = "Industrial District" },
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest
                {
                    Login = "zemill",
                    RewardTitle = "ReplayId",
                    ReplayId = ReplayId,
                    RedemptionId = Guid.NewGuid(),
                },
            },
        };

    private sealed class Opens : IMatchPredictionService
    {
        public int Count { get; private set; }

        public Task<bool> OpenAsync(int replayId, string map, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(true);
        }

        public Task RetryPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveTeamAsync(int? winningTeam, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveReplayAsync(
            int replayId,
            int? winningTeam,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task<PredictionReconcileResult> ReconcileAsync(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task TestAsync(int? winningTeam, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private class SilentTwitch : DispatchProxy
    {
        public static ITwitchClient Create()
        {
            return DispatchProxy.Create<ITwitchClient, SilentTwitch>();
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.ReturnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            return null;
        }
    }
}
