using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Heroes.Element;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesData;

/// <summary>heroes-data2 talents as <see cref="HeroTalent"/> values (#412), read the way GameData reads them.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroTalentsTests
{
    private const string HeroData = """
        {
          "meta": { "heroesVersion": "2.57.0.98304", "hdpVersion": "5.1.0", "itemsType": "Data", "dataType": "HeroData", "localizedText": "Extracted", "totalItems": 1 },
          "items": {
            "Wizard": {
              "unitId": "HeroWizard",
              "hyperlinkId": "LiMing",
              "talents": {
                "Level7": [
                  { "linkId": "WizardTeleportCalamity|WizardTeleportCalamityTalent|E|Level7", "talentId": "WizardTeleportCalamity", "buttonId": "WizardTeleportCalamityTalent", "abilityId": "WizardTeleport", "icon": "storm_ui_icon_wizard_teleport.png", "abilityType": "E", "sort": 3 },
                  { "linkId": "WizardZeisVengeance|WizardZeisVengeanceTalent|Q|Level7", "talentId": "WizardZeisVengeance", "buttonId": "WizardZeisVengeanceTalent", "abilityId": "WizardMagicMissiles", "icon": "storm_ui_icon_wizard_magicmissiles.png", "abilityType": "Q", "sort": 2 }
                ],
                "Level1": [
                  { "linkId": "WizardAetherWalker|WizardAetherWalkerTalent|E|Level1", "talentId": "WizardAetherWalker", "buttonId": "WizardAetherWalkerTalent", "abilityId": "WizardTeleport", "icon": "storm_ui_icon_wizard_teleport.png", "abilityType": "E", "sort": 2 }
                ]
              }
            }
          }
        }
        """;

    private const string GameStrings = """
        {
          "meta": { "heroesVersion": "2.57.0.98304", "hdpVersion": "5.1.0", "itemsType": "GameStrings", "dataTypes": [ "HeroData" ],
                    "gameStringText": { "locale": "ENUS", "textType": "RawText", "constantVars": { "replaced": true, "preserved": true }, "styleVars": { "replaced": true, "preserved": true } } },
          "items": {
            "hero": { "name": { "Wizard": "Li-Ming" } },
            "talent": {
              "name": {
                "WizardTeleportCalamity|WizardTeleportCalamityTalent|E|Level7": "Calamity",
                "WizardZeisVengeance|WizardZeisVengeanceTalent|Q|Level7": "Zei's Vengeance",
                "WizardAetherWalker|WizardAetherWalkerTalent|E|Level1": "Aether Walker"
              }
            }
          }
        }
        """;

    [Fact(Skip = GameDataCatalogTests.HeroesElementReflection)]
    public void Read_ListsTalentsByTierThenColumn_WithEnglishNames()
    {
        IReadOnlyList<HeroTalent> talents = WizardTalents();

        Assert.Equal(
            new[] { "WizardAetherWalker", "WizardZeisVengeance", "WizardTeleportCalamity" },
            talents.Select(talent => talent.TalentId)
        );
        HeroTalent calamity = talents[2];
        Assert.Equal(
            new HeroTalent(
                "WizardTeleportCalamity",
                "WizardTeleportCalamityTalent",
                2,
                3,
                "Calamity",
                "storm_ui_icon_wizard_teleport.png"
            ),
            calamity
        );
        Assert.Equal(0, talents[0].Tier);
        Assert.Equal("Zei's Vengeance", talents[1].Name);
    }

    [Fact]
    public void Read_WithoutAHero_IsEmpty()
    {
        Assert.Empty(HeroTalents.Read(null));
    }

    private static IReadOnlyList<HeroTalent> WizardTalents()
    {
        using GameStringsDocument strings = GameStringsDocument.Load(
            JsonDocument.Parse(GameStrings)
        );
        using HeroDataDocument document = HeroDataDocument.Load(
            JsonDocument.Parse(HeroData),
            strings
        );
        return HeroTalents.Read(document.GetElements().Single());
    }
}
