namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Hero skins in YouTube metadata (<c>YouTube:Titles:Skins</c>). Only notable skins are named:
/// Legendary, animated or voiced, or released within <see cref="NewSkinDays"/> of the game.
/// Every video lists them under the talent builds; only a hero focus (gameplay) video puts a
/// new skin in its title.
/// </summary>
public class SkinSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Days after release that a skin counts as new. Zero turns the new rule off.</summary>
    public int NewSkinDays { get; set; } = 30;

    /// <summary>A hero focus video's title names its hero's skin when the skin is new.</summary>
    public bool FocusTitles { get; set; } = true;
}
