using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.Replays;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

/// <summary>The download role's focus cards (#433): one a step, newest first, pruned when done.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusCardRefreshTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-refresh-" + Guid.NewGuid().ToString("N")
    );
    private readonly List<string> parsed = new();

    public FocusCardRefreshTests() => Directory.CreateDirectory(Standard);

    public void Dispose() => TestTemp.Delete(data);

    private string Standard => Path.Combine(data, "Standard");

    [Fact]
    public void Step_BuildsTheNewestMissingCard_OneAStep_ThenNothing()
    {
        Waiting(65550001, 65550003, 65550002);
        FocusCardRefresh refresh = Refresh();

        Assert.Equal(65550003, refresh.Step().ReplayId);
        Assert.Equal(65550002, refresh.Step().ReplayId);
        Assert.Equal(65550001, refresh.Step().ReplayId);
        Assert.Null(refresh.Step());

        Assert.Equal(3, parsed.Count);
        Assert.Equal(new[] { "65550001", "65550002", "65550003" }, refresh.Cards.Keys());
        Assert.Equal("Diamond 3", refresh.Cards.Read("65550003").Rank);
        Assert.Equal("Hero 0", refresh.Cards.Read("65550003").Players[0].Hero);
    }

    [Fact]
    public void Step_DeletesTheCardsOfSpectatedQuarantinedOrMissingReplays()
    {
        Waiting(65550001, 65550002, 65550003);
        FocusCardRefresh refresh = Refresh();
        while (refresh.Step() != null) { }

        File.AppendAllText(Path.Combine(data, SpectateQueue.SpectatedFileName), "65550003\n");
        File.AppendAllText(Path.Combine(data, SpectateQueue.QuarantineFileName), "65550002\n");
        File.Delete(Directory.GetFiles(Standard, "65550001_*").Single());

        Assert.Null(refresh.Step());
        Assert.Empty(refresh.Cards.Keys());
        Assert.Empty(refresh.Waiting());
    }

    [Fact]
    public void Step_BuildsAgain_WhenTheCardsVersionOrTheBuildReferenceChanged()
    {
        Waiting(65550001);
        FocusCardRefresh refresh = Refresh();
        FocusCard card = refresh.Step();
        card.Version = FocusCard.CurrentVersion - 1;
        refresh.Cards.Write(card);

        Assert.NotNull(refresh.Step());
        Assert.Null(refresh.Step());
        Assert.NotNull(Refresh(guideCheckedAt: "2026-11-01").Step());
        Assert.Equal(3, parsed.Count);
    }

    [Fact]
    public void Step_SkipsAReplayThatDoesNotParse_AndNeverTriesItAgain()
    {
        Waiting(65550001, 65550002);
        FocusCardRefresh refresh = Refresh(unreadable: "65550002");

        Assert.Equal(65550001, refresh.Step().ReplayId);
        Assert.Null(refresh.Step());
        Assert.Equal(1, parsed.Count(path => path.Contains("65550002", StringComparison.Ordinal)));
    }

    [Fact]
    public void Step_AddsEachNewCardToTheHeroBaselinesOnce()
    {
        Waiting(65550001, 65550002);
        FocusCardRefresh refresh = Refresh();
        refresh.Step();
        refresh.Step();

        HeroBaselines baselines = new HeroBaselineStore(HeroBaselineStore.PathFor(data)).Read();
        Assert.Equal(2, baselines.Folded.Count);
        // Hero 0 is a tank without an entry of its own: the tank list, so its takedowns count.
        Assert.Equal(2, baselines.Samples("H0", "Takedowns"));
        Assert.Equal(0, baselines.Samples("H0", "HeroDamage"));
    }

    private void Waiting(params int[] ids)
    {
        foreach (int id in ids)
        {
            File.WriteAllText(
                Path.Combine(
                    Standard,
                    $"{id}_Storm League_Diamond 3_Alterac Pass_abc_.StormReplay"
                ),
                "replay"
            );
        }
    }

    private FocusCardRefresh Refresh(
        string guideCheckedAt = "2026-10-12",
        string unreadable = null
    ) =>
        new(
            new FocusCardBuilder(FocusReplays.NoGuides),
            new FocusCardStore(FocusCardStore.DirectoryFor(data)),
            new HeroBaselineStore(HeroBaselineStore.PathFor(data)),
            HeroSignatures.Embedded(),
            new HeroFocusSettings(),
            () => FocusReplays.Catalog,
            path =>
            {
                parsed.Add(path);
                return unreadable != null && path.Contains(unreadable, StringComparison.Ordinal)
                    ? null
                    : FocusReplays.Replay();
            },
            Standard,
            data,
            guideCheckedAt,
            NullLogger.Instance
        );
}
