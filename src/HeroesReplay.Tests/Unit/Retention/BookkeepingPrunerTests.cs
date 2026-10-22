using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

/// <summary>
/// #492: the bookkeeping that grew without bound on the stream PC. Leases, player lookups and
/// unreadable copies go from the housekeeping pass; the prediction ledger and the reservation
/// ledger are trimmed by the process that owns each file.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class BookkeepingPrunerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-bookkeeping-" + Guid.NewGuid().ToString("N")
    );

    public BookkeepingPrunerTests() => Directory.CreateDirectory(root);

    public void Dispose() => TestTemp.Delete(root);

    [Fact]
    public void Leases_GoAfterLeaseKeepDays()
    {
        string old = Write(Path.Combine(root, "leases", "65538796.txt"), Now.AddDays(-5));
        string fresh = Write(Path.Combine(root, "leases", "65600000.txt"), Now.AddHours(-3));

        PrunePlan plan = ReplayLease.Plan(root, keepDays: 2, Now);

        PruneItem item = Assert.Single(plan.Items);
        Assert.Equal(old, item.Path);
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void CorruptCopies_KeepTheNewestPerFile_AndNoneOlderThanCorruptKeepDays()
    {
        string ledger = Path.Combine(root, "publication-reservations.txt");
        string[] copies = Enumerable
            .Range(1, 5)
            .Select(day =>
                Write(ledger + DurableFile.CorruptSuffix + (1000 + day), Now.AddDays(-day))
            )
            .ToArray();
        string ancient = Write(
            Path.Combine(root, "leases", "1.txt" + DurableFile.CorruptSuffix + "42"),
            Now.AddDays(-40)
        );
        string notOne = Write(Path.Combine(root, "notes.corrupt-copy"), Now.AddDays(-90));

        PrunePlan plan = CorruptFiles.Plan(root, keepCount: 3, keepDays: 30, Now);

        Assert.Equal(
            new[] { copies[3], copies[4], ancient }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        Assert.DoesNotContain(plan.Items, item => item.Path == notOne);
    }

    [Fact]
    public void PlayerLookups_GoWhenNoLookupWouldReuseThem_PrivateMarksStayLonger()
    {
        var store = new PlayerHeroStatsStore(Path.Combine(root, "players"));
        var settings = new PlayerStatsSettings
        {
            MaxAge = TimeSpan.FromDays(7),
            UnknownRetryAfter = TimeSpan.FromDays(1),
            PrivateKeep = TimeSpan.FromDays(30),
        };
        var known = new PlayerHeroLookup(PlayerHeroLookupStatus.Known, Reason: "test");
        store.Write("Old#1234", 2, "Jaina", known, Now.AddDays(-10));
        store.Write("Fresh#1234", 2, "Jaina", known, Now.AddDays(-2));
        store.Write(
            "Hidden#1234",
            2,
            null,
            new PlayerHeroLookup(PlayerHeroLookupStatus.Private, Reason: "test"),
            Now.AddDays(-10)
        );
        store.NoteCall(Now.AddDays(-60));
        File.SetLastWriteTimeUtc(
            Path.Combine(store.Folder, PlayerHeroStatsStore.CallsFileName),
            Now.AddDays(-60).UtcDateTime
        );

        PrunePlan plan = store.PlanPrune(Now, settings);

        PruneItem item = Assert.Single(plan.Items);
        Assert.Equal(store.PathFor("Old#1234", 2), item.Path);
        Assert.Contains("MaxAge", item.Reason);

        PrunePlan later = store.PlanPrune(Now.AddDays(25), settings);

        Assert.Contains(later.Items, entry => entry.Path == store.PathFor("Hidden#1234", 2));
        Assert.DoesNotContain(
            later.Items,
            entry => Path.GetFileName(entry.Path) == PlayerHeroStatsStore.CallsFileName
        );
    }

    [Fact]
    public void PredictionLedger_TrimSettled_DropsOnlyOldSettledEntries()
    {
        string path = Path.Combine(root, PredictionLedger.FileName);
        PredictionLedger ledger = PredictionLedger.Load(path);
        ledger.Upsert(Entry(1, PredictionLedgerState.Settled, Now.AddDays(-40)));
        ledger.Upsert(Entry(2, PredictionLedgerState.Terminal, Now.AddDays(-40)));
        ledger.Upsert(Entry(3, PredictionLedgerState.Open, Now.AddDays(-40)));
        ledger.Upsert(Entry(4, PredictionLedgerState.Settled, Now.AddDays(-2)));

        int dropped = ledger.TrimSettled(Now.AddDays(-30));

        Assert.Equal(2, dropped);
        Assert.Equal(
            new[] { 3, 4 },
            PredictionLedger.Load(path).Entries.Select(entry => entry.ReplayId).Order()
        );
    }

    [Fact]
    public void Reservations_Trim_DropsPastSlots_AndKeepsFutureSlotsAndTerminalMarks()
    {
        string path = PublicationReservation.PathFor(root);
        File.WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                "reserved|2026-09-01T12:00:00.0000000+00:00|0|replay-1",
                "reserved|2026-10-20T12:00:00.0000000+00:00|0|replay-2",
                "reserved|2026-10-25T12:00:00.0000000+00:00|1|replay-3",
                "terminal|replay-4"
            ) + Environment.NewLine
        );

        int dropped = PublicationReservation.Trim(path, Now.AddDays(-30));

        Assert.Equal(1, dropped);
        Assert.Null(PublicationReservation.HeldAt(path, "replay-1"));
        Assert.NotNull(PublicationReservation.HeldAt(path, "replay-2"));
        Assert.NotNull(PublicationReservation.HeldAt(path, "replay-3"));
        Assert.Contains("terminal|replay-4", File.ReadAllText(path));
        Assert.Equal(0, PublicationReservation.Trim(path, Now.AddDays(-30)));
    }

    private static PredictionLedgerEntry Entry(
        int replayId,
        PredictionLedgerState state,
        DateTimeOffset created
    ) =>
        new()
        {
            ReplayId = replayId,
            Attempt = 1,
            State = state,
            CreatedAt = created,
        };

    private static string Write(string path, DateTimeOffset written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
