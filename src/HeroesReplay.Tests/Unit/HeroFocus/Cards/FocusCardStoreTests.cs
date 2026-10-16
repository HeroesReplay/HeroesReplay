using System;
using System.IO;
using System.Linq;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusCardStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-cards-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(folder);

    [Fact]
    public void Write_ThenRead_GivesTheSameCard()
    {
        var store = new FocusCardStore(folder);
        FocusCard card = FocusFixtures.Card(
            65550003,
            FocusFixtures.Player(
                0,
                "Li-Ming",
                "Wiza",
                "Ranged Assassin",
                strength: FocusFixtures.Strength(80, 120)
            )
        );

        store.Write(card);
        FocusCard read = store.Read("65550003");

        Assert.Equal(65550003, read.ReplayId);
        Assert.Equal("Alterac Pass", read.Map);
        Assert.Equal(10, read.Players.Count);
        Assert.Equal("Li-Ming", read.Players[0].Hero);
        Assert.Equal(60000, read.Players[0].Stat("HeroDamage"));
        Assert.Equal(FocusStrengthStatus.Known, read.Players[0].Strength.Status);
        Assert.Equal(80.0 / 120, read.Players[0].Strength.WinRate);
        Assert.Equal(new[] { "65550003" }, store.Keys());
        Assert.Single(store.ReadAll());
    }

    [Fact]
    public void AnUnreadableCard_IsMissing_AndStaysWhereItIs()
    {
        var store = new FocusCardStore(folder);
        Directory.CreateDirectory(folder);
        string path = store.PathFor("65550004");
        File.WriteAllText(path, "{ not json");

        Assert.Null(store.Read("65550004"));
        Assert.Empty(store.ReadAll());
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void Delete_RemovesTheCard_AndKeysAreSafeFileNames()
    {
        var store = new FocusCardStore(folder);
        store.Write(
            FocusFixtures.Card(1, FocusFixtures.Player(0, "Valla", "Demo", "Ranged Assassin"))
        );

        Assert.True(store.Delete("1"));
        Assert.False(store.Delete("1"));
        Assert.Null(store.Read("1"));
        Assert.Equal("a_b_c.json", FocusCardStore.FileNameFor("a/b:c"));
        Assert.Empty(store.ReadAll());
        Assert.Equal(Path.Combine("data", "Focus", "cards"), FocusCardStore.DirectoryFor("data"));
        Assert.Empty(new FocusCardStore(Path.Combine(folder, "missing")).Keys().ToList());
    }
}
