using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Tests.Unit.Support;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Ledger;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusLedgerStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-ledger-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(folder);

    private string LedgerPath => Path.Combine(folder, "ledger.json");

    [Fact]
    public void Update_ThenRead_GivesTheEntries()
    {
        var store = new FocusLedgerStore(LedgerPath);

        int count = store.Update(ledger =>
        {
            ledger.Put(Entry("Wiza", "Ranged Assassin", Now, key: "1"));
            return ledger.Entries.Count;
        });

        Assert.Equal(1, count);
        FocusLedgerEntry read = Assert.Single(store.Read().Entries);
        Assert.Equal("Wiza", read.AttributeId);
        Assert.Equal(FocusEntryState.Played, read.State);
    }

    [Fact]
    public void TwoStoresOnOneFile_LoseNoUpdate()
    {
        var one = new FocusLedgerStore(LedgerPath);
        var two = new FocusLedgerStore(LedgerPath);

        Parallel.For(
            0,
            20,
            i =>
                (i % 2 == 0 ? one : two).Update(ledger =>
                {
                    ledger.Put(Entry("Hero" + i, "Tank", Now, key: i.ToString()));
                    return 0;
                })
        );

        Assert.Equal(20, one.Read().Entries.Select(entry => entry.Key).Distinct().Count());
    }

    [Fact]
    public void TheLock_IsNamedFromTheFile_NeverTheBarePrefix()
    {
        var store = new FocusLedgerStore(LedgerPath);
        var other = new FocusLedgerStore(Path.Combine(folder, "other.json"));

        Assert.StartsWith(
            FocusLedgerStore.MutexPrefix + ".",
            store.MutexName,
            StringComparison.Ordinal
        );
        Assert.NotEqual(store.MutexName, other.MutexName);
        Assert.Equal(
            store.MutexName,
            new FocusLedgerStore(LedgerPath.ToUpperInvariant()).MutexName
        );
    }

    [Fact]
    public void AnUnreadableLedger_ReadsAsEmpty_AndOnlyTheWriterSetsItAside()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(LedgerPath, "{ broken");
        var store = new FocusLedgerStore(LedgerPath);

        Assert.Empty(store.Read().Entries);
        Assert.Single(Directory.GetFiles(folder));

        store.Update(ledger =>
        {
            ledger.Put(Entry("Wiza", "Ranged Assassin", Now, key: "1"));
            return 0;
        });

        Assert.Single(store.Read().Entries);
        Assert.Single(Directory.GetFiles(folder, "ledger.json.corrupt-*"));
    }

    [Fact]
    public void Put_ReplacesTheReplaysEntry_AndHistoryCountsPickedAndPlayedOnly()
    {
        var ledger = new FocusLedger();
        ledger.Put(
            Entry(
                "Wiza",
                "Ranged Assassin",
                Now.AddHours(-3),
                key: "1",
                state: FocusEntryState.Picked
            )
        );
        ledger.Put(Entry("Wiza", "Ranged Assassin", Now.AddHours(-3), key: "1"));
        ledger.Put(
            Entry("Mura", "Tank", Now.AddHours(-2), key: "2", state: FocusEntryState.Abandoned)
        );
        ledger.Put(
            Entry("Uthe", "Healer", Now.AddHours(-1), key: "3", state: FocusEntryState.Reserved)
        );
        ledger.Put(
            Entry("Deha", "Bruiser", Now.AddHours(-4), key: "4", state: FocusEntryState.Picked)
        );

        Assert.Equal(4, ledger.Entries.Count);
        Assert.Equal(FocusEntryState.Played, ledger.Find("1").State);
        Assert.Equal(new[] { "4", "1" }, ledger.History.Select(entry => entry.Key));
        Assert.Null(ledger.Find("missing"));
    }
}
