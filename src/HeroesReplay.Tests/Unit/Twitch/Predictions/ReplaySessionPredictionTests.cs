using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch.Predictions;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySessionPredictionTests
{
    [Fact]
    public async Task StepAsync_OpeningPredictionStaysOnTheReplayTrace()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-session-prediction-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        string sessionPath = Path.Combine(directory, "replay-sessions.txt");
        var store = new SpectatorStatusStore(Path.Combine(directory, "status.json"));
        store.Patch(status =>
        {
            status.SpectatorRunning = true;
            status.Phase = "TimerDetected";
            status.ReplayId = 424242;
            status.Map = "Tomb of the Spider Queen";
            status.SnapshotStale = false;
        });

        using ActivityListener listener = Listen();
        ActivityTraceId replayTrace;
        using (Activity session = HeroesReplayTelemetry.BeginReplaySession(424242))
        {
            Assert.NotNull(session);
            replayTrace = session.TraceId;
            ReplaySessionFile.Publish(session, 424242, sessionPath);
        }

        var predictions = new TracePredictions();
        var watcher = new StatusPredictionWatcher(
            NullLogger<StatusPredictionWatcher>.Instance,
            new AppSettings(),
            store,
            predictions,
            SilentTwitch.Create()
        )
        {
            ReplaySessionFilePath = sessionPath,
        };

        using var ambient = new Activity("process");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();
        try
        {
            await watcher.StepAsync(new PredictionSessionTracker(), CancellationToken.None);
        }
        finally
        {
            ambient.Stop();
            Directory.Delete(directory, recursive: true);
        }

        Assert.Equal(replayTrace, predictions.OpenedOn);
        Assert.NotEqual(ambient.TraceId, predictions.OpenedOn);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == HeroesReplayTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class TracePredictions : IMatchPredictionService
    {
        public ActivityTraceId? OpenedOn { get; private set; }

        public Task<bool> OpenAsync(int replayId, string map, CancellationToken cancellationToken)
        {
            OpenedOn = Activity.Current?.TraceId;
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
