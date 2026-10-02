using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Team-composition labels for one team: double soak, siege, dive, poke, melee assassins, all
/// melee or one ranged, sustain, specialists, merc control, and glass cannon. The rules read the
/// heroes-data2 role, playstyles (after <c>HeroTagOverrides</c>), <c>isMelee</c>, and ratings.
/// A team with a hero the catalog cannot match, or fewer than five heroes, has no labels.
/// </summary>
public static class TeamComposition
{
    public const int TeamSize = 5;

    public const string SplitPush = "SplitPush";
    public const string Siege = "Siege";
    public const string Dive = "Dive";
    public const string Poke = "Poke";
    public const string MeleeAssassins = "MeleeAssassins";
    public const string AllMelee = "AllMelee";
    public const string OneRanged = "OneRanged";
    public const string Sustain = "Sustain";
    public const string Specialists = "Specialists";
    public const string MercControl = "MercControl";
    public const string GlassCannon = "GlassCannon";

    /// <summary>Every label key, in the order the rules run and the notes are written.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        SplitPush,
        Siege,
        Dive,
        Poke,
        MeleeAssassins,
        AllMelee,
        OneRanged,
        Sustain,
        Specialists,
        MercControl,
        GlassCannon,
    };

    public const string SoloLaner = "SoloLaner";
    public const string TowerPusher = "TowerPusher";
    public const string WaveClearer = "WaveClearer";
    public const string Ganker = "Ganker";
    public const string Escaper = "Escaper";
    public const string RoleCaster = "RoleCaster";
    public const string AllyHealer = "AllyHealer";
    public const string SelfHealer = "SelfHealer";
    public const string RoleSpecialist = "RoleSpecialist";
    public const string MercKiller = "MercKiller";

    /// <summary>
    /// Every label the team's heroes match, in <see cref="Keys"/> order, whatever its corpus
    /// frequency. Use <see cref="Unusual"/> for the labels a title may name.
    /// </summary>
    public static IReadOnlyList<DraftNote> Labels(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        int team,
        YouTubeTitleSettings titles = null
    )
    {
        titles ??= new YouTubeTitleSettings();
        TeamCompositionSettings settings = titles.Compositions ?? new TeamCompositionSettings();
        List<Hero> heroes = TeamHeroes(catalog, roster, team);
        if (!settings.Enabled || heroes == null)
        {
            return Array.Empty<DraftNote>();
        }

        var tags = new List<ISet<string>>(heroes.Count);
        var melee = new List<bool?>(heroes.Count);
        foreach (Hero hero in heroes)
        {
            tags.Add(Tags(hero, settings));
            melee.Add(IsMelee(hero, settings));
        }

        // Only SoloLaner counts. A tower pusher that also clears waves is not a soaker: Kael'thas,
        // Jaina, Sylvanas, and Sgt. Hammer have both tags, and with them 77% of games matched.
        var labels = new List<DraftNote>();
        CompositionRule rule = settings.SplitPush;
        if (On(rule) && Count(tags, t => t.Contains(SoloLaner)) >= rule.MinHeroes)
        {
            Add(labels, SplitPush, rule.Label);
        }

        rule = settings.Siege;
        if (
            On(rule)
            && Count(tags, t => t.Contains(TowerPusher) || t.Contains(WaveClearer))
                >= rule.MinHeroes
        )
        {
            Add(labels, Siege, rule.Label);
        }

        rule = settings.Dive;
        if (
            On(rule)
            && Count(tags, t => t.Contains(Ganker)) >= rule.MinHeroes
            && Count(tags, t => t.Contains(Escaper)) >= rule.MinEscapers
        )
        {
            Add(labels, Dive, rule.Label);
        }

        rule = settings.Poke;
        if (On(rule))
        {
            int casters = 0;
            for (int i = 0; i < heroes.Count; i++)
            {
                if (melee[i] == false && tags[i].Contains(RoleCaster))
                {
                    casters++;
                }
            }

            if (casters >= rule.MinHeroes)
            {
                Add(labels, Poke, rule.Label);
            }
        }

        rule = settings.MeleeAssassins;
        if (On(rule))
        {
            string role = HeroDraft.Label(titles.MeleeAssassin, HeroDraft.MeleeAssassin);
            int assassins = 0;
            foreach (Hero hero in heroes)
            {
                if (HeroDraft.Is(hero, role))
                {
                    assassins++;
                }
            }

            if (assassins >= rule.MinHeroes)
            {
                Add(labels, MeleeAssassins, Counted(assassins, rule.Label));
            }
        }

        AddMelee(labels, melee, settings);

        rule = settings.Sustain;
        if (
            On(rule)
            && Count(tags, t => t.Contains(AllyHealer) || t.Contains(SelfHealer)) >= rule.MinHeroes
        )
        {
            Add(labels, Sustain, rule.Label);
        }

        rule = settings.Specialists;
        if (On(rule))
        {
            int specialists = Count(tags, t => t.Contains(RoleSpecialist));
            if (specialists >= rule.MinHeroes)
            {
                Add(labels, Specialists, Counted(specialists, rule.Label));
            }
        }

        rule = settings.MercControl;
        if (On(rule) && Count(tags, t => t.Contains(MercKiller)) >= rule.MinHeroes)
        {
            Add(labels, MercControl, rule.Label);
        }

        AddGlassCannon(labels, heroes, settings.GlassCannon);
        return labels;
    }

    /// <summary>
    /// The labels that are rare enough to name: those whose corpus frequency is below
    /// <see cref="TeamCompositionSettings.MaxFrequency"/>.
    /// </summary>
    public static IReadOnlyList<DraftNote> Unusual(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        int team,
        YouTubeTitleSettings titles = null
    )
    {
        titles ??= new YouTubeTitleSettings();
        TeamCompositionSettings settings = titles.Compositions ?? new TeamCompositionSettings();
        var unusual = new List<DraftNote>();
        foreach (DraftNote label in Labels(catalog, roster, team, titles))
        {
            if (settings.IsUnusual(label.Key))
            {
                unusual.Add(label);
            }
        }

        return unusual;
    }

    /// <summary>The hero's playstyles after its <c>HeroTagOverrides</c> entry, if it has one.</summary>
    public static ISet<string> Tags(Hero hero, TeamCompositionSettings settings)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hero == null)
        {
            return tags;
        }

        foreach (string tag in hero.Descriptors)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag.Trim());
            }
        }

        HeroTagOverride change = Override(hero, settings?.HeroTagOverrides);
        if (change == null)
        {
            return tags;
        }

        foreach (string tag in change.Add ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag.Trim());
            }
        }

        foreach (string tag in change.Remove ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Remove(tag.Trim());
            }
        }

        return tags;
    }

    /// <summary>The hero's <c>isMelee</c>, or its <c>HeroTagOverrides</c> entry's <c>IsMelee</c>.</summary>
    public static bool? IsMelee(Hero hero, TeamCompositionSettings settings)
    {
        if (hero == null)
        {
            return null;
        }

        return Override(hero, settings?.HeroTagOverrides)?.IsMelee ?? hero.IsMelee;
    }

    private static HeroTagOverride Override(
        Hero hero,
        IReadOnlyDictionary<string, HeroTagOverride> overrides
    )
    {
        if (overrides == null)
        {
            return null;
        }

        foreach (KeyValuePair<string, HeroTagOverride> pair in overrides)
        {
            if (
                pair.Value != null
                && (
                    HeroDraft.SameName(pair.Key, hero.Name)
                    || HeroDraft.SameName(pair.Key, hero.HyperlinkId)
                    || HeroDraft.SameName(pair.Key, hero.UnitId)
                    || HeroDraft.SameName(pair.Key, hero.AttributeId)
                )
            )
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static List<Hero> TeamHeroes(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        int team
    )
    {
        if (catalog == null || catalog.Count == 0 || roster == null || roster.Count == 0)
        {
            return null;
        }

        var heroes = new List<Hero>();
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || player.Team != team || string.IsNullOrWhiteSpace(player.Hero))
            {
                continue;
            }

            Hero hero = HeroDraft.Find(catalog, player.Hero);
            if (hero == null)
            {
                return null;
            }

            heroes.Add(hero);
        }

        return heroes.Count < TeamSize ? null : heroes;
    }

    private static void AddMelee(
        List<DraftNote> labels,
        List<bool?> heroes,
        TeamCompositionSettings settings
    )
    {
        int melee = 0;
        foreach (bool? hero in heroes)
        {
            if (hero == null)
            {
                return;
            }

            if (hero.Value)
            {
                melee++;
            }
        }

        if (On(settings.AllMelee) && melee >= settings.AllMelee.MinHeroes)
        {
            Add(labels, AllMelee, settings.AllMelee.Label);
        }
        else if (On(settings.OneRanged) && melee >= settings.OneRanged.MinHeroes)
        {
            Add(labels, OneRanged, settings.OneRanged.Label);
        }
    }

    private static void AddGlassCannon(
        List<DraftNote> labels,
        List<Hero> heroes,
        CompositionRule rule
    )
    {
        if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Label))
        {
            return;
        }

        double damage = 0;
        double survivability = 0;
        foreach (Hero hero in heroes)
        {
            if (hero.Ratings == null)
            {
                return;
            }

            damage += hero.Ratings.Damage;
            survivability += hero.Ratings.Survivability;
        }

        if (
            survivability / heroes.Count <= rule.MaxSurvivability
            && damage / heroes.Count >= rule.MinDamage
        )
        {
            Add(labels, GlassCannon, rule.Label);
        }
    }

    private static bool On(CompositionRule rule) =>
        rule != null
        && rule.Enabled
        && rule.MinHeroes > 0
        && !string.IsNullOrWhiteSpace(rule.Label);

    private static int Count(List<ISet<string>> tags, Func<ISet<string>, bool> match)
    {
        int count = 0;
        foreach (ISet<string> set in tags)
        {
            if (match(set))
            {
                count++;
            }
        }

        return count;
    }

    private static string Counted(int count, string label)
    {
        return HeroDraft.Counted(count, label.Trim().ToLowerInvariant());
    }

    private static void Add(List<DraftNote> labels, string key, string text)
    {
        string clean = text?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return;
        }

        labels.Add(new DraftNote(key, char.ToUpperInvariant(clean[0]) + clean.Substring(1)));
    }
}
