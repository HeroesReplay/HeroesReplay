using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Tests.Unit.HeroFocus;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>
/// The spectate queue with focus picks on (#433): every third Standard replay is the best focus
/// candidate, requests still play first and do not count, and a pick survives a requeue and a
/// restart but not a defer.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusQueueTests : IDisposable
{
    private static readonly HeroSignatures Signatures = HeroSignatures.Embedded();
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-queue-" + Guid.NewGuid().ToString("N")
    );
    private readonly AppSettings settings;

    public FocusQueueTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Standard"));
        Directory.CreateDirectory(Path.Combine(root, "Requests"));
        File.WriteAllText(Path.Combine(root, SpectateQueue.SpectatedFileName), string.Empty);
        settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            StormReplay = new StormReplaySettings { Seperator = "_" },
            Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98285" },
            Focus = new HeroFocusSettings { Enabled = true, Every = 3 },
        };
    }

    public void Dispose() => TestTemp.Delete(root);

    [Fact]
    public async Task EveryThirdStandardReplay_IsTheFocus_AndTheStandoutWaitsForIt()
    {
        Standard(101, 102, 103, 104, 105);
        Card(105, "Li-Ming", "Wiza", "Ranged Assassin");
        ReplayCacheProvider provider = Provider();

        LoadedReplay first = await provider.TryLoadNextReplayAsync();
        provider.MarkSpectated(first);
        LoadedReplay second = await provider.TryLoadNextReplayAsync();
        provider.MarkSpectated(second);
        LoadedReplay focus = await provider.TryLoadNextReplayAsync();
        provider.MarkSpectated(focus);
        LoadedReplay after = await provider.TryLoadNextReplayAsync();

        Assert.Equal(104, first.ReplayId);
        Assert.Null(first.FocusPick);
        Assert.Equal(103, second.ReplayId);
        Assert.Equal(105, focus.ReplayId);
        Assert.Equal("Li-Ming (Teleport Build)", focus.FocusPick.Label);
        Assert.Equal(0, ReplayFocus.PlayerIndex(focus));
        Assert.Null(after.FocusPick);
        FocusLedger ledger = Ledger();
        Assert.Equal(FocusEntryState.Played, ledger.Find("105").State);
        Assert.Empty(ledger.SinceFocus);
    }

    [Fact]
    public async Task AWaitingRequest_PlaysFirst_WithoutAFocus_AndDoesNotCount()
    {
        Standard(101, 105);
        Request(200);
        Card(105, "Li-Ming", "Wiza", "Ranged Assassin");
        FocusPicker picker = Due(Picker());
        ReplayCacheProvider provider = Provider(picker);

        LoadedReplay request = await provider.TryLoadNextReplayAsync();
        provider.MarkSpectated(request);
        LoadedReplay focus = await provider.TryLoadNextReplayAsync();

        Assert.Equal(200, request.ReplayId);
        Assert.Null(request.FocusPick);
        Assert.Equal(105, focus.ReplayId);
        Assert.NotNull(focus.FocusPick);
        Assert.DoesNotContain(200, Ledger().SinceFocus);
    }

    [Fact]
    public async Task ADeferredFocus_IsAbandoned_AndTheNextPickIsStillAFocus()
    {
        Standard(103, 104, 105);
        Card(105, "Li-Ming", "Wiza", "Ranged Assassin");
        Card(103, "Muradin", "Mura", "Tank");
        ReplayCacheProvider provider = Provider(Due(Picker()));

        LoadedReplay deferred = await provider.TryLoadNextReplayAsync();
        provider.Defer(deferred);
        LoadedReplay next = await provider.TryLoadNextReplayAsync();

        Assert.Equal(105, deferred.ReplayId);
        Assert.Equal(FocusEntryState.Abandoned, Ledger().Find("105").State);
        Assert.Equal(103, next.ReplayId);
        Assert.Equal("Muradin", next.FocusPick.Hero);
    }

    [Fact]
    public async Task AFocusPick_SurvivesARequeueAndARestart()
    {
        Standard(103, 104, 105);
        Card(105, "Li-Ming", "Wiza", "Ranged Assassin");
        ReplayCacheProvider provider = Provider(Due(Picker()));

        LoadedReplay picked = await provider.TryLoadNextReplayAsync();
        provider.Requeue(picked);
        LoadedReplay requeued = await provider.TryLoadNextReplayAsync();
        LoadedReplay restarted = await Provider().TryLoadNextReplayAsync();

        Assert.Same(picked, requeued);
        Assert.NotNull(requeued.FocusPick);
        Assert.Equal(105, restarted.ReplayId);
        Assert.Equal(picked.FocusPick, restarted.FocusPick);
    }

    [Fact]
    public async Task WithFocusOff_TheQueueIsUnchanged()
    {
        Standard(103, 104, 105);
        Card(105, "Li-Ming", "Wiza", "Ranged Assassin");
        settings.Focus.Enabled = false;
        ReplayCacheProvider provider = Provider();

        LoadedReplay first = await provider.TryLoadNextReplayAsync();
        provider.MarkSpectated(first);

        Assert.Equal(105, first.ReplayId);
        Assert.Null(first.FocusPick);
        Assert.False(File.Exists(FocusLedgerStore.PathFor(root)));
    }

    private ReplayCacheProvider Provider(FocusPicker picker = null)
    {
        var provider = new ReplayCacheProvider(
            NullLogger<ReplayCacheProvider>.Instance,
            new StarLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new IdleProfile(),
            new CancellationTokenProvider(),
            settings,
            picker ?? Picker()
        );
        provider.UseInstalledVersions(() => new[] { "2.57.0.98348" });
        return provider;
    }

    private FocusPicker Picker() =>
        new(
            settings.Focus,
            root,
            TimeSpan.FromDays(7),
            Signatures,
            NullLogger<FocusPicker>.Instance,
            () => Now
        );

    private static FocusPicker Due(FocusPicker picker)
    {
        picker.Played(1, null, false);
        picker.Played(2, null, false);
        return picker;
    }

    private FocusLedger Ledger() => new FocusLedgerStore(FocusLedgerStore.PathFor(root)).Read();

    private void Standard(params int[] ids)
    {
        foreach (int id in ids)
        {
            File.WriteAllBytes(
                Path.Combine(root, "Standard", id + "_Storm League_Alterac Pass_.StormReplay"),
                new byte[] { 1 }
            );
        }
    }

    private void Request(int id) =>
        File.WriteAllBytes(
            Path.Combine(root, "Requests", id + "_Storm League_Alterac Pass_.StormReplay"),
            new byte[] { 1 }
        );

    /// <summary>A card whose star (player 0) is <c>Star{id}#{id}</c>; the other nine are AI.</summary>
    private void Card(int id, string hero, string attributeId, string role)
    {
        FocusCard card = FocusFixtures.Card(
            id,
            Player(
                0,
                hero,
                attributeId,
                role,
                build: hero == "Li-Ming" ? "Teleport Build" : "Main Build",
                name: "Star" + id,
                battleTag: id,
                awards: new[] { "MVP" }
            )
        );
        foreach (FocusCardPlayer other in card.Players.Skip(1))
        {
            other.IsAi = true;
        }

        new FocusCardStore(FocusCardStore.DirectoryFor(root)).Write(card);
    }

    /// <summary>Every replay is on the installed build, with <c>Star{id}#{id}</c> as player 0.</summary>
    private sealed class StarLoader : IReplayLoader
    {
        public Task<Replay> LoadAsync(string path)
        {
            int id = int.Parse(Path.GetFileName(path).Split('_')[0]);
            return Task.FromResult(
                new Replay
                {
                    ReplayVersion = "2.57.0.98348",
                    Map = "Alterac Pass",
                    Players = Enumerable
                        .Range(0, 10)
                        .Select(index => new Player
                        {
                            Name = index == 0 ? "Star" + id : "Other" + index,
                            BattleTag = index == 0 ? id : 2000 + index,
                        })
                        .ToArray(),
                }
            );
        }
    }

    private sealed class IdleProfile : IHeroesProfileService
    {
        public Task<int> GetMaxReplayIdAsync() => Task.FromResult(0);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult<HeroesProfileReplay>(null);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(ReplayListing.Empty);

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
