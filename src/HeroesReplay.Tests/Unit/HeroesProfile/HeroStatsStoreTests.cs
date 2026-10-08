using System;
using System.IO;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class HeroStatsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Fetched = new DateTimeOffset(
        2026,
        10,
        8,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-herostats-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Theory]
    [InlineData("2.57.0.98348", "2.57")]
    [InlineData("2.57", "2.57")]
    [InlineData(" 2.55.17.98025 ", "2.55")]
    [InlineData("2", null)]
    [InlineData("x.57", null)]
    [InlineData(null, null)]
    public void Major_IsTheFirstTwoNumbers(string version, string major)
    {
        Assert.Equal(major, HeroStatsPatch.Major(version));
    }

    [Theory]
    [InlineData("Storm League", "sl")]
    [InlineData("StormLeague", "sl")]
    [InlineData("sl", "sl")]
    [InlineData("Quick Match", "qm")]
    [InlineData("ARAM", "ar")]
    [InlineData("Unranked Draft", "ud")]
    [InlineData("Brawl", null)]
    [InlineData("", null)]
    public void GameTypeCode_ReadsNamesAndCodes(string gameType, string code)
    {
        Assert.Equal(code, HeroStatsPatch.GameTypeCode(gameType));
    }

    [Fact]
    public void PathFor_NamesTheMajorPatchAndGameTypeUnderHeroesProfile()
    {
        var store = new HeroStatsStore(data);

        Assert.Equal(
            Path.Combine(data, "HeroesProfile", "hero-stats", "2.57-sl.json"),
            store.PathFor("2.57.0.98348", "Storm League")
        );
        Assert.Null(store.PathFor("2.57.0.98348", "Brawl"));
        Assert.Null(store.PathFor(null, "sl"));
    }

    [Fact]
    public void ReadFresh_ReturnsTheSnapshotInsideMaxAgeOnly()
    {
        var store = new HeroStatsStore(data);
        store.Write(Snapshot("2.57", Fetched));

        Assert.NotNull(
            store.ReadFresh(
                "2.57.0.98348",
                "Storm League",
                Fetched.AddHours(71),
                TimeSpan.FromHours(72)
            )
        );
        Assert.Null(
            store.ReadFresh(
                "2.57.0.98348",
                "Storm League",
                Fetched.AddHours(73),
                TimeSpan.FromHours(72)
            )
        );
        Assert.Null(
            store.ReadFresh("2.57.0.98348", "Quick Match", Fetched, TimeSpan.FromHours(72))
        );
        Assert.Null(store.ReadFresh("2.57.0.98348", "Storm League", Fetched, TimeSpan.Zero));
        // A file dated well in the future is not trusted.
        Assert.Null(store.ReadFresh("2.57", "sl", Fetched.AddHours(-1), TimeSpan.FromHours(72)));
    }

    [Fact]
    public void IsDue_WhenMissingOrOlderThanTheRefreshInterval()
    {
        var store = new HeroStatsStore(data);
        Assert.True(store.IsDue("2.57", "sl", Fetched, TimeSpan.FromHours(24)));

        store.Write(Snapshot("2.57", Fetched));

        Assert.False(store.IsDue("2.57", "sl", Fetched.AddHours(23), TimeSpan.FromHours(24)));
        Assert.True(store.IsDue("2.57", "sl", Fetched.AddHours(25), TimeSpan.FromHours(24)));
    }

    [Fact]
    public void Read_ReturnsNullForAFileThatIsNotASnapshot()
    {
        var store = new HeroStatsStore(data);
        string path = store.PathFor("2.57", "sl");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "{ not json");

        Assert.Null(store.Read("2.57", "sl"));
        Assert.True(store.IsDue("2.57", "sl", Fetched, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void Read_SeesAFileReplacedByANewerWrite()
    {
        var store = new HeroStatsStore(data);
        store.Write(Snapshot("2.57", Fetched));
        Assert.Equal(Fetched, store.Read("2.57", "sl").FetchedAtUtc);

        HeroStatsSnapshot newer = Snapshot("2.57", Fetched.AddDays(1));
        newer.Heroes.Add(
            new HeroStats
            {
                AttributeId = "HXAL",
                Name = "Xal'atath",
                Wins = 1,
                Games = 2,
            }
        );
        store.Write(newer);

        HeroStatsSnapshot read = store.Read("2.57", "sl");
        Assert.Equal(Fetched.AddDays(1), read.FetchedAtUtc);
        Assert.NotNull(read.Find("HXAL"));
    }

    [Fact]
    public void Prune_DeletesEveryPatchItDoesNotKeep()
    {
        var store = new HeroStatsStore(data);
        store.Write(Snapshot("2.57", Fetched));
        store.Write(Snapshot("2.55", Fetched));
        store.Write(Snapshot("2.53", Fetched));

        var deleted = store.Prune(new[] { "2.57", "2.55.17.98025" });

        Assert.Equal(new[] { "2.53-sl.json" }, deleted);
        Assert.NotNull(store.Read("2.57", "sl"));
        Assert.NotNull(store.Read("2.55", "sl"));
        Assert.Null(store.Read("2.53", "sl"));
        Assert.Empty(store.Prune(Array.Empty<string>()));
    }

    private static HeroStatsSnapshot Snapshot(string patch, DateTimeOffset fetched) =>
        new HeroStatsSnapshot
        {
            Patch = patch,
            GameType = "sl",
            FetchedAtUtc = fetched,
            Heroes =
            {
                new HeroStats
                {
                    AttributeId = "Demo",
                    Name = "Valla",
                    Wins = 3629,
                    Games = 7226,
                },
            },
        };
}
