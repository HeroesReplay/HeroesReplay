using System;
using HeroesReplay.Core.HeroesData;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesData;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SkinCatalogTests
{
    internal const string SkinData = """
        {"meta":{"itemsType":"Data","dataType":"SkinData"},"items":{
          "MephistoMoophisto":{"attributeId":"MMO1","rarity":"Legendary","releaseDate":"2026-11-23","features":["ThemedAbilities","ThemedAnimations"],"variationSkinIds":["MephistoMoophistoVar1"]},
          "MephistoMoophistoVar1":{"attributeId":"MMO2","rarity":"Legendary","releaseDate":"2026-11-23","features":["ThemedAbilities","ThemedAnimations"]},
          "MephistoStarWraith":{"attributeId":"MEH4","rarity":"Epic","releaseDate":"2018-09-06","features":["AlteredVO","ThemedAbilities"]},
          "MephistoVar1":{"attributeId":"MEH0","rarity":"Rare","releaseDate":"2018-09-04"},
          "TyraelMecha":{"attributeId":"Tyr1","rarity":"Legendary","releaseDate":"2016-01-12","features":["ReplacedVO"]},
          "AbathurBaseVar3":{"attributeId":"Aba0","rarity":"Rare","releaseDate":"2026-11-10"},
          "NoName":{"attributeId":"Non0","rarity":"Legendary","releaseDate":"2020-01-01"}
        }}
        """;

    internal const string GameStrings = """
        {"meta":{"itemsType":"GameStrings"},"items":{"skin":{"name":{
          "MephistoMoophisto":"Moophisto",
          "MephistoMoophistoVar1":"Holy Holstein Moophisto",
          "MephistoStarWraith":"Star Wraith Mephisto",
          "MephistoVar1":"Hollow Mephisto",
          "TyraelMecha":"Mecha Tyrael",
          "AbathurBaseVar3":"Kaldir Abathur"
        }}}}
        """;

    private static readonly DateTime Played = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Read_NamesTintsAfterTheirBaseSkin()
    {
        SkinCatalog catalog = SkinCatalog.Read(SkinData, GameStrings);

        Skin tint = catalog.Find("MephistoMoophistoVar1");
        Assert.Equal("Holy Holstein Moophisto", tint.Name);
        Assert.Equal("Moophisto", tint.BaseName);
        Assert.Equal("Moophisto", catalog.Find("MephistoMoophisto").BaseName);
        Assert.Equal(new DateTime(2026, 11, 23), tint.ReleaseDate);
    }

    [Fact]
    public void Find_DefaultUnknownAndUnnamedSkins_AreNull()
    {
        SkinCatalog catalog = SkinCatalog.Read(SkinData, GameStrings);

        Assert.Null(catalog.Find(null));
        Assert.Null(catalog.Find(""));
        Assert.Null(catalog.Find("MephistoNotYetInTheData"));
        Assert.Null(catalog.Find("NoName"));
        Assert.Equal(6, catalog.Count);
    }

    [Fact]
    public void Notable_IsLegendaryAnimatedVoicedOrNew()
    {
        SkinCatalog catalog = SkinCatalog.Read(SkinData, GameStrings);

        Assert.True(catalog.Find("MephistoMoophisto").IsNotable(Played, 30));
        Assert.True(catalog.Find("TyraelMecha").IsNotable(Played, 30));
        Assert.True(catalog.Find("MephistoStarWraith").IsNotable(Played, 30));
        Assert.False(catalog.Find("MephistoVar1").IsNotable(Played, 30));
        // A plain Rare skin counts only while it is new.
        Assert.True(catalog.Find("AbathurBaseVar3").IsNotable(Played, 30));
        Assert.False(catalog.Find("AbathurBaseVar3").IsNotable(Played, 0));
    }

    [Fact]
    public void IsNew_CountsDaysFromRelease()
    {
        Skin moophisto = SkinCatalog.Read(SkinData, GameStrings).Find("MephistoMoophisto");

        Assert.True(moophisto.IsNew(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), 30));
        Assert.True(moophisto.IsNew(new DateTime(2026, 12, 23, 23, 0, 0, DateTimeKind.Utc), 30));
        Assert.False(moophisto.IsNew(new DateTime(2026, 12, 24, 0, 0, 0, DateTimeKind.Utc), 30));
        Assert.False(moophisto.IsNew(new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc), 30));
        Assert.False(moophisto.IsNew(null, 30));
    }

    [Fact]
    public void Read_EmptyOrMissingData_IsEmpty()
    {
        Assert.Equal(0, SkinCatalog.Read(null, GameStrings).Count);
        Assert.Equal(0, SkinCatalog.Read("{}", GameStrings).Count);
        Assert.Equal(0, SkinCatalog.Read(SkinData, "{}").Count);
    }
}
