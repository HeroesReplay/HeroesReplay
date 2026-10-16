using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.ChatMessages;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.ChatMessages;

/// <summary>
/// The focus replay's chat line (#433): once per replay when its match clock runs, the hero and
/// build only, and only while the chat bot is on.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class HeroFocusChatTests : IDisposable
{
    private const int ReplayId = 65550101;

    private static readonly FocusPick LiMing = new(
        ReplayId,
        "65550101",
        3,
        "Star#1234",
        "Li-Ming",
        "Wiza",
        "Ranged Assassin",
        "Teleport Build"
    );

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-chat-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(directory);

    [Fact]
    public void TheLine_IsSentOnce_WhenTheMatchClockRuns()
    {
        var chat = new HeroFocusChat();
        SpectatorStatus status = Status(Focused(LiMing));

        Assert.Null(chat.Next(status));
        status.Phase = "TimerDetected";
        Assert.Equal("Hero focus: this game follows Li-Ming (Teleport Build).", chat.Next(status));
        Assert.Null(chat.Next(status));
    }

    [Fact]
    public void NoLine_ForAStaleStatus_AnotherReplay_OrAnOrdinaryReplay()
    {
        SpectatorStatus stale = Status(Focused(LiMing), "TimerDetected");
        stale.SnapshotStale = true;
        SpectatorStatus moved = Status(Focused(LiMing), "TimerDetected");
        moved.ReplayId = ReplayId + 1;

        Assert.Null(new HeroFocusChat().Next(stale));
        Assert.Null(new HeroFocusChat().Next(moved));
        Assert.Null(
            new HeroFocusChat().Next(
                Status(new LoadedReplay { ReplayId = ReplayId }, "TimerDetected")
            )
        );
    }

    [Fact]
    public void WithoutAClearBuild_TheLineNamesTheHeroAlone()
    {
        SpectatorStatus status = Status(Focused(LiMing with { Build = null }), "TimerDetected");

        Assert.Equal("Hero focus: this game follows Li-Ming.", new HeroFocusChat().Next(status));
    }

    [Fact]
    public void TheStatus_CarriesTheFocusButNeverThePlayer_AndNoneForARequest()
    {
        SpectatorHeroFocusStatus focus = SpectatorHeroFocusStatus.From(Focused(LiMing));
        LoadedReplay request = Focused(LiMing);
        request.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest { Login = "viewer", ReplayId = ReplayId },
        };

        Assert.Equal(ReplayId, focus.ReplayId);
        Assert.Equal("Li-Ming", focus.Hero);
        Assert.Equal("Teleport Build", focus.Build);
        Assert.Equal("Ranged Assassin", focus.Role);
        Assert.Null(SpectatorHeroFocusStatus.From(request));
        Assert.Null(SpectatorHeroFocusStatus.From(new LoadedReplay { ReplayId = ReplayId }));
    }

    [Fact]
    public void TheWatcher_SendsTheLineOnce_AndOnlyWithTheChatBotOn()
    {
        SpectatorStatusStore store = Store(Focused(LiMing));
        var sent = new List<string>();
        StatusPredictionWatcher on = Watcher(store, chatBot: true, sent);
        StatusPredictionWatcher off = Watcher(store, chatBot: false, sent);
        var chat = new HeroFocusChat();

        on.AnnounceHeroFocus(chat);
        on.AnnounceHeroFocus(chat);
        off.AnnounceHeroFocus(new HeroFocusChat());

        Assert.Equal(new[] { "Hero focus: this game follows Li-Ming (Teleport Build)." }, sent);
    }

    private SpectatorStatusStore Store(LoadedReplay loaded)
    {
        var store = new SpectatorStatusStore(Path.Combine(directory, "status.json"));
        store.Patch(status => GameManager.ShowLoading(status, loaded, current: null));
        store.Patch(status =>
        {
            status.Phase = "TimerDetected";
            status.SnapshotStale = false;
        });
        return store;
    }

    private static StatusPredictionWatcher Watcher(
        SpectatorStatusStore store,
        bool chatBot,
        List<string> sent
    ) =>
        new(
            NullLogger<StatusPredictionWatcher>.Instance,
            new AppSettings
            {
                Twitch = new TwitchSettings { EnableChatBot = chatBot, Channel = "dev" },
            },
            store,
            new NoPredictions(),
            RecordingTwitch.Create(sent)
        );

    private static SpectatorStatus Status(LoadedReplay loaded, string phase = "Loading")
    {
        var status = new SpectatorStatus();
        GameManager.ShowLoading(status, loaded, current: null);
        status.Phase = phase;
        return status;
    }

    private static LoadedReplay Focused(FocusPick pick) =>
        new()
        {
            ReplayId = ReplayId,
            Replay = new Replay(),
            FocusPick = pick,
        };

    private sealed class NoPredictions : IMatchPredictionService
    {
        public Task<bool> OpenAsync(
            int replayId,
            string map,
            PredictionKind kind,
            System.Threading.CancellationToken cancellationToken
        ) => Task.FromResult(false);

        public Task RetryPendingAsync(System.Threading.CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StartAsync(
            LoadedReplay replay,
            System.Threading.CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task ResolveAsync(
            LoadedReplay replay,
            System.Threading.CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task ResolveTeamAsync(
            int? winningTeam,
            System.Threading.CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task ResolveReplayAsync(
            int replayId,
            int? winningTeam,
            System.Threading.CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task<PredictionReconcileResult> ReconcileAsync(
            System.Threading.CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task TestAsync(
            int? winningTeam,
            System.Threading.CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    /// <summary>Records each chat message; every other call does nothing.</summary>
    public class RecordingTwitch : DispatchProxy
    {
        private List<string> sent;

        public static ITwitchClient Create(List<string> sent)
        {
            ITwitchClient client = DispatchProxy.Create<ITwitchClient, RecordingTwitch>();
            ((RecordingTwitch)(object)client).sent = sent;
            return client;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == nameof(ITwitchClient.SendMessage) && args[1] is string text)
            {
                sent.Add(text);
            }

            return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
        }
    }
}
