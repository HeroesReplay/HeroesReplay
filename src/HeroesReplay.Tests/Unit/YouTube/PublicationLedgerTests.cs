using System;
using System.IO;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationLedgerTests
{
    [Fact]
    public void Save_RoundTripsTheInsertTimes()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-ledger-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var when = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
            PublicationLedgerStore.Save(
                directory,
                new PublicationLedger
                {
                    InsertsThisQuotaDay = 4,
                    StuckPrivate = 1,
                    LastPublicUtc = when,
                    PublicAtUtc = { when },
                    Requested = { true },
                }
            );

            PublicationLedger loaded = PublicationLedgerStore.Load(directory);

            Assert.Equal(4, loaded.InsertsThisQuotaDay);
            Assert.Equal(1, loaded.StuckPrivate);
            Assert.Equal(when, loaded.LastPublicUtc);
            Assert.Equal(when, loaded.PublicAtUtc[0]);
            Assert.True(loaded.Requested[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
