using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Tests.Unit.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus;

/// <summary>The download role's focus step (#433): a card, its records, the privacy feed.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class HeroFocusDownloadTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-download-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(data);

    [Fact]
    public async Task Step_BuildsACard_FillsItsRecords_AndStartsThePrivacyCursor()
    {
        string standard = Path.Combine(data, "Standard");
        Directory.CreateDirectory(standard);
        File.WriteAllText(
            Path.Combine(standard, "65550001_Storm League_Gold 1_Alterac Pass_x_.StormReplay"),
            "replay"
        );
        var api = new FakeHeroesProfileApi();
        api.Answer(
            PlayerHeroStatsFetch.PlayersHeroesPath,
            FakeHeroesProfileApi.Ok("[{\"wins\":12,\"losses\":8,\"games_played\":20}]")
        );
        var caller = new HeroesProfileCaller(
            api,
            api.Pacer(),
            NullLogger.Instance,
            api.Clock,
            api.Delay
        );
        var store = new PlayerHeroStatsStore(PlayerHeroStatsStore.FolderFor(data));
        var players = new PlayerStatsSettings();
        var focus = new HeroFocusSettings { StrengthLookupsPerStep = 4 };
        var cards = new FocusCardRefresh(
            new FocusCardBuilder(FocusReplays.NoGuides),
            new FocusCardStore(FocusCardStore.DirectoryFor(data)),
            new HeroBaselineStore(HeroBaselineStore.PathFor(data)),
            HeroSignatures.Embedded(),
            focus,
            () => FocusReplays.Catalog,
            _ => FocusReplays.Replay(),
            standard,
            data,
            "2026-10-12",
            NullLogger.Instance
        );
        var download = new HeroFocusDownload(
            cards,
            new FocusStrengthRefresh(
                new PlayerHeroStatsFetch(caller, store, players, NullLogger.Instance, api.Clock),
                focus,
                players,
                NullLogger.Instance,
                () => api.Now.UtcDateTime
            ),
            new PlayerPrivacySync(caller, store, players, NullLogger.Instance, api.Clock),
            focus,
            NullLogger<HeroFocusDownload>.Instance
        );

        await download.StepAsync(CancellationToken.None);

        FocusCard card = cards.Cards.Read("65550001");
        Assert.NotNull(card);
        Assert.Equal(4, card.Players.FindAll(player => player.Strength?.IsKnown == true).Count);
        Assert.Equal(4, api.Calls.Count);
        Assert.NotNull(store.ReadPrivacy()?.Since);
    }

    [Fact]
    public async Task Step_BuildsSeveralCardsBeforeAnyLookup()
    {
        string standard = Path.Combine(data, "Standard");
        Directory.CreateDirectory(standard);
        foreach (int id in new[] { 65550001, 65550002, 65550003 })
        {
            File.WriteAllText(
                Path.Combine(standard, $"{id}_Storm League_Gold 1_Alterac Pass_x_.StormReplay"),
                "replay"
            );
        }

        var api = new FakeHeroesProfileApi();
        api.Answer(
            PlayerHeroStatsFetch.PlayersHeroesPath,
            FakeHeroesProfileApi.Ok("[{\"wins\":12,\"losses\":8,\"games_played\":20}]")
        );
        var caller = new HeroesProfileCaller(
            api,
            api.Pacer(),
            NullLogger.Instance,
            api.Clock,
            api.Delay
        );
        var store = new PlayerHeroStatsStore(PlayerHeroStatsStore.FolderFor(data));
        var players = new PlayerStatsSettings();
        var focus = new HeroFocusSettings { CardsPerStep = 2, StrengthLookupsPerStep = 0 };
        var cards = new FocusCardRefresh(
            new FocusCardBuilder(FocusReplays.NoGuides),
            new FocusCardStore(FocusCardStore.DirectoryFor(data)),
            new HeroBaselineStore(HeroBaselineStore.PathFor(data)),
            HeroSignatures.Embedded(),
            focus,
            () => FocusReplays.Catalog,
            _ => FocusReplays.Replay(),
            standard,
            data,
            "2026-10-12",
            NullLogger.Instance
        );
        var download = new HeroFocusDownload(
            cards,
            new FocusStrengthRefresh(
                new PlayerHeroStatsFetch(caller, store, players, NullLogger.Instance, api.Clock),
                focus,
                players,
                NullLogger.Instance,
                () => api.Now.UtcDateTime
            ),
            new PlayerPrivacySync(caller, store, players, NullLogger.Instance, api.Clock),
            focus,
            NullLogger<HeroFocusDownload>.Instance
        );

        await download.StepAsync(CancellationToken.None);

        Assert.Equal(new[] { "65550002", "65550003" }, cards.Cards.Keys());
        Assert.Empty(api.Calls);
    }
}
