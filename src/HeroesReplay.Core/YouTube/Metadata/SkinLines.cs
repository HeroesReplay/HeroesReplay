using System;
using System.Collections.Generic;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// The skin wording of YouTube metadata. The description uses the full name, tint included
/// (<c>Holy Holstein Moophisto</c>); titles and tags use the base skin's name
/// (<c>Moophisto</c>).
/// </summary>
public static class SkinLines
{
    /// <summary>The player's skin when the metadata names it, else null.</summary>
    public static Skin Notable(
        ReplayMediaPlayer player,
        SkinCatalog skins,
        DateTime? played,
        SkinSettings settings
    )
    {
        if (player == null || skins == null || settings?.Enabled != true)
        {
            return null;
        }

        Skin skin = skins.Find(player.Skin);
        return skin != null && skin.IsNotable(played, settings.NewSkinDays) ? skin : null;
    }

    /// <summary>
    /// <c>Blue skins: Holy Holstein Moophisto (Mephisto), Mecha Tyrael</c> and the same for Red;
    /// a team without a notable skin has no line.
    /// </summary>
    public static IReadOnlyList<string> Description(
        IReadOnlyList<ReplayMediaPlayer> roster,
        SkinCatalog skins,
        DateTime? played,
        SkinSettings settings
    )
    {
        var lines = new List<string>();
        AddTeam(lines, "Blue", 0, roster, skins, played, settings);
        AddTeam(lines, "Red", 1, roster, skins, played, settings);
        return lines;
    }

    /// <summary>
    /// <c>Moophisto skin</c> for a hero focus title when the focused player's skin is new, else
    /// null.
    /// </summary>
    public static string FocusTitle(
        ReplayMediaPlayer focused,
        SkinCatalog skins,
        DateTime? played,
        SkinSettings settings
    )
    {
        if (settings?.FocusTitles != true)
        {
            return null;
        }

        Skin skin = Notable(focused, skins, played, settings);
        return skin != null && skin.IsNew(played, settings.NewSkinDays)
            ? skin.BaseName + " skin"
            : null;
    }

    /// <summary>
    /// The base names of every new skin in the game, and of the focused player's notable skin.
    /// </summary>
    public static IReadOnlyList<string> Tags(
        IReadOnlyList<ReplayMediaPlayer> roster,
        ReplayMediaPlayer focused,
        SkinCatalog skins,
        DateTime? played,
        SkinSettings settings
    )
    {
        var tags = new List<string>();
        Skin own = Notable(focused, skins, played, settings);
        if (own != null)
        {
            tags.Add(own.BaseName);
        }

        if (roster != null)
        {
            foreach (ReplayMediaPlayer player in roster)
            {
                Skin skin = Notable(player, skins, played, settings);
                if (skin != null && skin.IsNew(played, settings.NewSkinDays) && !tags.Contains(skin.BaseName))
                {
                    tags.Add(skin.BaseName);
                }
            }
        }

        return tags;
    }

    /// <summary>
    /// The skin's full name, with the hero in brackets when the name does not say it:
    /// <c>Moophisto (Mephisto)</c>, but <c>Mecha Tyrael</c>.
    /// </summary>
    public static string Named(Skin skin, string hero)
    {
        string name = skin.Name;
        return string.IsNullOrWhiteSpace(hero)
            || name.Contains(hero.Trim(), StringComparison.OrdinalIgnoreCase)
            ? name
            : name + " (" + hero.Trim() + ")";
    }

    private static void AddTeam(
        List<string> lines,
        string teamName,
        int team,
        IReadOnlyList<ReplayMediaPlayer> roster,
        SkinCatalog skins,
        DateTime? played,
        SkinSettings settings
    )
    {
        if (roster == null)
        {
            return;
        }

        var named = new List<string>();
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || player.Team != team)
            {
                continue;
            }

            Skin skin = Notable(player, skins, played, settings);
            if (skin != null)
            {
                named.Add(Named(skin, player.Hero));
            }
        }

        if (named.Count > 0)
        {
            lines.Add(teamName + " skins: " + string.Join(", ", named));
        }
    }
}
