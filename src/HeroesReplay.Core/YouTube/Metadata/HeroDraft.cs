using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// A short title note when the current hero-select roles are not the usual
/// one tank, one bruiser, one healer, and a ranged assassin. The role is the
/// single current role, so Johanna plus Chen is a tank and a bruiser.
/// </summary>
public static class HeroDraft
{
    public const string Tank = "Tank";
    public const string Bruiser = "Bruiser";
    public const string Healer = "Healer";
    public const string Support = "Support";
    public const string MeleeAssassin = "Melee Assassin";
    public const string RangedAssassin = "Ranged Assassin";

    public static string Phrase(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        YouTubeTitleSettings titles = null
    )
    {
        string blue = TeamNote(catalog, roster, titles, team: 0)?.Text;
        string red = TeamNote(catalog, roster, titles, team: 1)?.Text;
        if (blue == null && red == null)
        {
            return null;
        }

        if (blue != null && red != null && string.Equals(blue, red, StringComparison.Ordinal))
        {
            return blue;
        }

        if (blue == null)
        {
            return WithTeam("Red", red);
        }

        if (red == null)
        {
            return WithTeam("Blue", blue);
        }

        return WithTeam("Blue", blue) + ", " + WithTeam("Red", red);
    }

    /// <summary>
    /// One team's role note, or null for a usual draft, a short team, or a hero the catalog
    /// cannot match. The key is the note's switch name in <c>YouTube:Titles</c>.
    /// </summary>
    public static DraftNote TeamNote(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        YouTubeTitleSettings titles,
        int team
    )
    {
        titles ??= new YouTubeTitleSettings();
        if (
            !titles.DraftNotes
            || catalog == null
            || catalog.Count == 0
            || roster == null
            || roster.Count == 0
        )
        {
            return null;
        }

        string tank = Label(titles.Tank, Tank);
        string bruiser = Label(titles.Bruiser, Bruiser);
        string healer = Label(titles.Healer, Healer);
        string support = Label(titles.Support, Support);
        string melee = Label(titles.MeleeAssassin, MeleeAssassin);
        string rangedRole = Label(titles.RangedAssassin, RangedAssassin);
        int healers = 0;
        int tanks = 0;
        int bruisers = 0;
        int supports = 0;
        int ranged = 0;
        int named = 0;
        int matched = 0;
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || player.Team != team || string.IsNullOrWhiteSpace(player.Hero))
            {
                continue;
            }

            named++;
            Hero hero = Find(catalog, player.Hero);
            if (hero == null || string.IsNullOrWhiteSpace(hero.Role))
            {
                continue;
            }

            matched++;
            if (Is(hero, healer))
            {
                healers++;
            }
            else if (Is(hero, tank))
            {
                tanks++;
            }
            else if (Is(hero, bruiser))
            {
                bruisers++;
            }
            else if (Is(hero, support))
            {
                supports++;
            }
            else if (Is(hero, rangedRole))
            {
                ranged++;
            }
            else if (!Is(hero, melee))
            {
                return null;
            }
        }

        if (named == 0 || matched != named || matched < 4)
        {
            return null;
        }

        if (titles.NoTankOrHealer && healers == 0 && tanks == 0)
        {
            return new DraftNote(
                nameof(titles.NoTankOrHealer),
                "No " + Lower(tank) + " or " + Lower(healer)
            );
        }

        if (titles.NoHealer && healers == 0)
        {
            return new DraftNote(nameof(titles.NoHealer), "No " + Lower(healer));
        }

        if (titles.TripleHealer && healers >= 3)
        {
            return new DraftNote(nameof(titles.TripleHealer), Counted(healers, Lower(healer)));
        }

        if (titles.DoubleHealer && healers == 2)
        {
            return new DraftNote(nameof(titles.DoubleHealer), "Double " + Lower(healer));
        }

        if (titles.DoubleBruiserWithoutTank && tanks == 0 && bruisers >= 2)
        {
            return new DraftNote(
                nameof(titles.DoubleBruiserWithoutTank),
                Counted(bruisers, Lower(bruiser))
            );
        }

        if (titles.NoTank && tanks == 0)
        {
            return new DraftNote(nameof(titles.NoTank), "No " + Lower(tank));
        }

        if (titles.DoubleTank && tanks >= 2)
        {
            return new DraftNote(nameof(titles.DoubleTank), Counted(tanks, Lower(tank)));
        }

        if (titles.TripleBruiser && bruisers >= 3)
        {
            return new DraftNote(nameof(titles.TripleBruiser), Counted(bruisers, Lower(bruiser)));
        }

        if (titles.DoubleSupport && supports >= 2)
        {
            return new DraftNote(nameof(titles.DoubleSupport), Counted(supports, Lower(support)));
        }

        if (titles.NoRangedAssassin && ranged == 0)
        {
            return new DraftNote(nameof(titles.NoRangedAssassin), "No " + Lower(rangedRole));
        }

        return null;
    }

    internal static string Label(string configured, string fallback)
    {
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
    }

    private static string Lower(string label)
    {
        return label.ToLowerInvariant();
    }

    internal static string Counted(int count, string singular)
    {
        if (count == 2)
        {
            return "Double " + singular;
        }

        if (count == 3)
        {
            return "Triple " + singular;
        }

        return count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + singular
            + "s";
    }

    internal static string WithTeam(string team, string phrase)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            return phrase;
        }

        return team + " " + char.ToLowerInvariant(phrase[0]) + phrase.Substring(1);
    }

    /// <summary>The catalog hero by name, hyperlink id, unit id, or attribute id, ignoring case and punctuation.</summary>
    public static Hero Find(IReadOnlyList<Hero> catalog, string heroName)
    {
        string key = Key(heroName);
        if (key == null)
        {
            return null;
        }

        foreach (Hero hero in catalog)
        {
            if (hero == null)
            {
                continue;
            }

            if (
                Same(key, hero.Name)
                || Same(key, hero.HyperlinkId)
                || Same(key, hero.UnitId)
                || Same(key, hero.AttributeId)
            )
            {
                return hero;
            }
        }

        return null;
    }

    internal static bool SameName(string left, string right)
    {
        string key = Key(left);
        return key != null && Same(key, right);
    }

    private static bool Same(string key, string candidate)
    {
        string other = Key(candidate);
        if (other == null)
        {
            return false;
        }

        return key == other || key == "the" + other || other == "the" + key;
    }

    internal static string Key(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var chars = new char[value.Length];
        int count = 0;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                chars[count++] = char.ToLowerInvariant(character);
            }
        }

        return count == 0 ? null : new string(chars, 0, count);
    }

    internal static bool Is(Hero hero, string role)
    {
        return string.Equals(hero.Role, role, StringComparison.OrdinalIgnoreCase);
    }
}
