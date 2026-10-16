using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Picking;

/// <summary>
/// The spectator's focus picks (#433): the cadence, the ranking of the waiting replays, the pick
/// checked against the parsed replay, and the ledger through a restart, a defer and a play.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusPickerTests : IDisposable
{
    private static readonly HeroSignatures Signatures = HeroSignatures.Embedded();
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-pick-" + Guid.NewGuid().ToString("N")
    );
    private DateTime now = Now;

    public void Dispose() => TestTemp.Delete(root);

    [Fact]
    public void AFocusIsDue_AfterEveryMinusOneStandardReplays_AndARequestDoesNotCount()
    {
        FocusPicker picker = Picker();
        Write(Card(101, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        int[] waiting = { 101 };

        Assert.False(picker.Plan(waiting, requestWaiting: false).Due);
        picker.Played(1, null, requested: false);
        picker.Played(1, null, requested: false);
        picker.Played(2, null, requested: true);
        Assert.False(picker.Plan(waiting, requestWaiting: false).Due);
        picker.Played(3, null, requested: false);

        FocusPlan plan = picker.Plan(waiting, requestWaiting: false);
        Assert.True(plan.Due);
        Assert.Equal(new[] { 101 }, plan.Order);
        Assert.Equal(new[] { 1, 3 }, Ledger().SinceFocus);
        Assert.False(picker.Plan(waiting, requestWaiting: true).Due);
    }

    [Fact]
    public void ADuePlan_RanksOnlyWaitingAndRecentReplays_BestFirst()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(101, Star("Muradin", "Mura", "Tank", "Plain", 1001, kills: 2, assists: 6)));
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        Write(Card(103, Star("Uther", "Uthe", "Healer", "Gone", 1003)));
        FocusCard stale = Card(104, Star("Dehaka", "Deha", "Bruiser", "Old", 1004));
        stale.GameDateUtc = Now.AddDays(-8);
        Write(stale);

        FocusPlan plan = picker.Plan(new[] { 101, 102, 104 }, requestWaiting: false);

        Assert.True(plan.Due);
        Assert.Equal(new[] { 102, 101 }, plan.Order);
        Assert.Equal("Li-Ming", plan.Ranked[0].Hero);
    }

    [Fact]
    public void Attach_PicksTheRankedPlayer_WithAClearBuild_AndRecordsIt()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        FocusPlan plan = picker.Plan(new[] { 102 }, requestWaiting: false);

        FocusPick pick = picker.Attach(102, Parsed("Star", 1234), requested: false, plan);

        Assert.NotNull(pick);
        Assert.Equal(0, pick.PlayerIndex);
        Assert.Equal("Li-Ming (Teleport Build)", pick.Label);
        FocusLedgerEntry entry = Ledger().Find("102");
        Assert.Equal(FocusEntryState.Picked, entry.State);
        Assert.Equal("Star#1234", entry.Player);
        Assert.True(entry.BuildClear);
    }

    [Fact]
    public void Attach_NamesNoBuild_WhenTheTalentsOnlyLooselyFitOne()
    {
        FocusPicker picker = Due(Picker());
        FocusCardPlayer star = Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234);
        star.Build.Score = 0.75;
        Write(Card(102, star));
        FocusPlan plan = picker.Plan(new[] { 102 }, requestWaiting: false);

        FocusPick pick = picker.Attach(102, Parsed("Star", 1234), requested: false, plan);

        Assert.Equal("Li-Ming", pick.Label);
        Assert.Equal("Teleport Build", Ledger().Find("102").Build);
    }

    [Fact]
    public void Attach_RefusesARequest_AReplayThatDoesNotMatch_AndAPlanThatIsNotDue()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        FocusPlan plan = picker.Plan(new[] { 102 }, requestWaiting: false);

        Assert.Null(picker.Attach(102, Parsed("Star", 1234), requested: true, plan));
        Assert.Null(picker.Attach(102, Parsed("Someone", 1234), requested: false, plan));
        Assert.Null(
            picker.Attach(102, Parsed("Star", 1234), requested: false, plan with { Due = false })
        );
        Assert.Null(Ledger().Find("102"));
    }

    [Fact]
    public void APlayedFocus_StartsTheCountAgain()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        FocusPick pick = picker.Attach(
            102,
            Parsed("Star", 1234),
            false,
            picker.Plan(new[] { 102 }, false)
        );

        picker.Played(102, pick, requested: false);

        FocusLedger ledger = Ledger();
        Assert.Equal(FocusEntryState.Played, ledger.Find("102").State);
        Assert.Equal(Now, ledger.Find("102").PlayedAtUtc);
        Assert.Empty(ledger.SinceFocus);
        Assert.False(picker.Plan(new[] { 103 }, false).Due);
    }

    [Fact]
    public void AnAbandonedPick_LeavesTheFocusDue()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        FocusPick pick = picker.Attach(
            102,
            Parsed("Star", 1234),
            false,
            picker.Plan(new[] { 102 }, false)
        );

        picker.Abandon(pick, "its replay was deferred");

        Assert.Equal(FocusEntryState.Abandoned, Ledger().Find("102").State);
        Assert.True(picker.Plan(new[] { 102 }, false).Due);
    }

    [Fact]
    public void APickMadeBeforeARestart_IsKept_UntilItExpires()
    {
        FocusPicker first = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        Write(Card(103, Star("Muradin", "Mura", "Tank", "Other", 1003)));
        first.Attach(102, Parsed("Star", 1234), false, first.Plan(new[] { 102, 103 }, false));

        FocusPicker restarted = Picker();
        FocusPlan plan = restarted.Plan(new[] { 102, 103 }, false);
        FocusPick kept = restarted.Attach(102, Parsed("Star", 1234), false, FocusPlan.Off);

        Assert.Equal(102, plan.Order[0]);
        Assert.Equal("Li-Ming (Teleport Build)", kept.Label);

        now = Now.AddHours(13);
        FocusPlan later = restarted.Plan(new[] { 102, 103 }, false);
        Assert.Equal(FocusEntryState.Abandoned, Ledger().Find("102").State);
        Assert.Null(restarted.Attach(102, Parsed("Star", 1234), false, FocusPlan.Off));
        Assert.True(later.Due);
    }

    [Fact]
    public void AKeptPickWhosePlayerIsGone_IsAbandoned()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        picker.Attach(102, Parsed("Star", 1234), false, picker.Plan(new[] { 102 }, false));

        Assert.Null(picker.Attach(102, Parsed("Other", 99), false, FocusPlan.Off));
        Assert.Equal(FocusEntryState.Abandoned, Ledger().Find("102").State);
    }

    [Fact]
    public void APickThatPlayedWithoutItsFocus_CoversNothing_AndCountsAsOrdinary()
    {
        FocusPicker picker = Due(Picker());
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));
        picker.Attach(102, Parsed("Star", 1234), false, picker.Plan(new[] { 102 }, false));

        picker.Played(102, null, requested: false);

        Assert.Equal(FocusEntryState.Abandoned, Ledger().Find("102").State);
        Assert.Contains(102, Ledger().SinceFocus);
    }

    [Fact]
    public void WhileNoFocusIsDue_AStandoutIsReserved_AndAnOrdinaryCandidateIsNot()
    {
        FocusPicker picker = Picker();
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));

        FocusPlan fresh = picker.Plan(new[] { 102 }, false);
        FocusPicker strict = Picker(
            new HeroFocusSettings { Enabled = true, ReserveNoveltyAt = 1.1 }
        );

        Assert.False(fresh.Due);
        Assert.Equal(102, fresh.Reserved);
        Assert.Empty(fresh.Order);
        Assert.Null(strict.Plan(new[] { 102 }, false).Reserved);
    }

    [Fact]
    public void ADueFocusWithNoEligiblePlayer_PlansNothing()
    {
        FocusPicker picker = Due(Picker());
        FocusCardPlayer weak = Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234);
        weak.Strength = FocusFixtures.Strength(10, 30);
        Write(Card(102, weak));

        FocusPlan plan = picker.Plan(new[] { 102 }, false);

        Assert.True(plan.Due);
        Assert.Empty(plan.Order);
        Assert.Empty(plan.Ranked);
    }

    [Fact]
    public void TheLedger_KeepsNoHeroesProfileNumbers()
    {
        FocusPicker picker = Due(Picker());
        FocusCardPlayer star = Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234);
        star.Strength = FocusFixtures.Strength(60, 80);
        star.Strength.HeroMmr = 2900;
        star.Strength.RankTier = "Diamond 2";
        Write(Card(102, star));

        picker.Attach(102, Parsed("Star", 1234), false, picker.Plan(new[] { 102 }, false));

        FocusStrength kept = Ledger().Find("102").Strength;
        Assert.Equal(FocusStrengthStatus.Known, kept.Status);
        Assert.Equal(0, kept.Wins);
        Assert.Equal(0, kept.Games);
        Assert.Null(kept.HeroMmr);
        Assert.Null(kept.RankTier);
    }

    [Fact]
    public void TheCadence_KeepsItsNewestIds()
    {
        FocusPicker picker = Picker();
        for (int id = 1; id <= FocusPicker.CadenceCap + 5; id++)
        {
            picker.Played(id, null, requested: false);
        }

        List<int> since = Ledger().SinceFocus;
        Assert.Equal(FocusPicker.CadenceCap, since.Count);
        Assert.Equal(6, since[0]);
    }

    [Fact]
    public void Off_PlansAndPicksNothing()
    {
        FocusPicker off = Picker(new HeroFocusSettings { Enabled = false });
        Write(Card(102, Star("Li-Ming", "Wiza", "Ranged Assassin", "Star", 1234)));

        Assert.Same(FocusPlan.Off, off.Plan(new[] { 102 }, false));
        Assert.Null(off.Attach(102, Parsed("Star", 1234), false, FocusPlan.Off));
        off.Played(5, null, false);
        Assert.False(File.Exists(FocusLedgerStore.PathFor(root)));
    }

    private FocusPicker Picker(HeroFocusSettings settings = null) =>
        new(
            settings ?? new HeroFocusSettings { Enabled = true },
            root,
            TimeSpan.FromDays(7),
            Signatures,
            NullLogger<FocusPicker>.Instance,
            () => now
        );

    /// <summary>Two ordinary replays played, so the next pick is a focus.</summary>
    private static FocusPicker Due(FocusPicker picker)
    {
        picker.Played(1, null, false);
        picker.Played(2, null, false);
        return picker;
    }

    private FocusLedger Ledger() => new FocusLedgerStore(FocusLedgerStore.PathFor(root)).Read();

    private void Write(FocusCard card)
    {
        // Only the star is a candidate: the other nine are AI.
        foreach (FocusCardPlayer other in card.Players.Skip(1))
        {
            other.IsAi = true;
        }

        new FocusCardStore(FocusCardStore.DirectoryFor(root)).Write(card);
    }

    private static FocusCardPlayer Star(
        string hero,
        string attributeId,
        string role,
        string name,
        int battleTag,
        int kills = 8,
        int assists = 12
    ) =>
        Player(
            0,
            hero,
            attributeId,
            role,
            kills: kills,
            assists: assists,
            build: hero == "Li-Ming" ? "Teleport Build" : "Main Build",
            name: name,
            battleTag: battleTag,
            awards: new[] { "MVP" }
        );

    /// <summary>A parsed replay whose first player is <c>name#battleTag</c>.</summary>
    private static Replay Parsed(string name, int battleTag) =>
        new()
        {
            Players = Enumerable
                .Range(0, 10)
                .Select(index =>
                    index == 0
                        ? new Player { Name = name, BattleTag = battleTag }
                        : new Player { Name = "Other" + index, BattleTag = 2000 + index }
                )
                .ToArray(),
        };
}
