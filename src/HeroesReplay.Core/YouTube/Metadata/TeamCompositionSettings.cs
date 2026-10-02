using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Team-composition labels (<c>YouTube:Titles:Compositions</c>). Each rule counts the heroes on
/// one team from their heroes-data2 role, playstyles, <c>isMelee</c>, and ratings. A label is
/// named only while its corpus share of games in <see cref="Frequencies"/> is below
/// <see cref="MaxFrequency"/>, so a label that half the games have never reaches a title.
/// </summary>
public class TeamCompositionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The highest share of games (0 to 1) a label may have in the corpus and still be named.
    /// </summary>
    public double MaxFrequency { get; set; } = 0.10;

    /// <summary>
    /// Double soak and split push: at least <c>MinHeroes</c> heroes that can hold a lane alone
    /// (<c>SoloLaner</c>). Two is the usual offlaner plus one soaker (45% of games), so the
    /// default is three.
    /// </summary>
    public CompositionRule SplitPush { get; set; } = new() { Label = "Split push", MinHeroes = 3 };

    /// <summary>At least <c>MinHeroes</c> tower pushers or wave clearers.</summary>
    public CompositionRule Siege { get; set; } = new() { Label = "Siege", MinHeroes = 4 };

    /// <summary>At least <c>MinHeroes</c> gankers, with at least <c>MinEscapers</c> escapers.</summary>
    public CompositionRule Dive { get; set; } =
        new()
        {
            Label = "Dive",
            MinHeroes = 4,
            MinEscapers = 4,
        };

    /// <summary>At least <c>MinHeroes</c> ranged casters.</summary>
    public CompositionRule Poke { get; set; } = new() { Label = "Poke", MinHeroes = 3 };

    /// <summary>
    /// At least <c>MinHeroes</c> melee assassins. The label is counted: <c>Double melee assassin</c>.
    /// </summary>
    public CompositionRule MeleeAssassins { get; set; } =
        new() { Label = "Melee assassin", MinHeroes = 2 };

    /// <summary>At least <c>MinHeroes</c> melee heroes. Wins over <see cref="OneRanged"/>.</summary>
    public CompositionRule AllMelee { get; set; } = new() { Label = "All melee", MinHeroes = 5 };

    /// <summary>At least <c>MinHeroes</c> melee heroes, when <see cref="AllMelee"/> did not match.</summary>
    public CompositionRule OneRanged { get; set; } = new() { Label = "One ranged", MinHeroes = 4 };

    /// <summary>At least <c>MinHeroes</c> heroes that heal allies or themselves, the healer included.</summary>
    public CompositionRule Sustain { get; set; } =
        new() { Label = "Triple sustain", MinHeroes = 3 };

    /// <summary>
    /// At least <c>MinHeroes</c> specialists (Abathur, Murky, Zagara, and similar). The label is
    /// counted: <c>Triple specialist</c>. Two is 18% of games, so the default is three.
    /// </summary>
    public CompositionRule Specialists { get; set; } =
        new() { Label = "Specialist", MinHeroes = 3 };

    /// <summary>At least <c>MinHeroes</c> merc killers.</summary>
    public CompositionRule MercControl { get; set; } =
        new() { Label = "Merc control", MinHeroes = 2 };

    /// <summary>
    /// Average survivability at most <c>MaxSurvivability</c> and average damage at least
    /// <c>MinDamage</c>.
    /// </summary>
    public CompositionRule GlassCannon { get; set; } =
        new()
        {
            Label = "Glass cannon",
            MaxSurvivability = 4.4,
            MinDamage = 7,
        };

    /// <summary>
    /// Share of games (0 to 1) that had each label in the corpus, keyed by the label key
    /// (<c>SplitPush</c>) or the role note's switch name (<c>NoTank</c>). A key that is
    /// missing is treated as rare. The defaults are <see cref="CorpusFrequencies"/>.
    /// </summary>
    public Dictionary<string, double> Frequencies { get; set; } =
        new(CorpusFrequencies, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Playstyle tags to add or remove per hero, keyed by hero name or hyperlink id. Applied
    /// before every rule. The defaults are <see cref="DefaultHeroTagOverrides"/>.
    /// </summary>
    public Dictionary<string, HeroTagOverride> HeroTagOverrides { get; set; } =
        DefaultHeroTagOverrides();

    /// <summary>
    /// The corpus report behind the defaults: <c>calculators compositions</c> over current-patch
    /// Storm League replays (issue #140). Share of games where either team had the note.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> CorpusFrequencies = new Dictionary<
        string,
        double
    >(StringComparer.OrdinalIgnoreCase)
    {
        // 295 Storm League games on build 2.57.0.98304 (replay ids 65635951 to 65645361).
        [TeamComposition.SplitPush] = 0.0475,
        [TeamComposition.Siege] = 0.0610,
        [TeamComposition.Dive] = 0.0373,
        [TeamComposition.Poke] = 0.0169,
        [TeamComposition.MeleeAssassins] = 0.0271,
        [TeamComposition.AllMelee] = 0,
        [TeamComposition.OneRanged] = 0.0644,
        [TeamComposition.Sustain] = 0.0237,
        [TeamComposition.Specialists] = 0.0102,
        [TeamComposition.MercControl] = 0.0712,
        [TeamComposition.GlassCannon] = 0.0102,
        [nameof(YouTubeTitleSettings.NoTankOrHealer)] = 0.0034,
        [nameof(YouTubeTitleSettings.NoHealer)] = 0.0136,
        [nameof(YouTubeTitleSettings.TripleHealer)] = 0,
        [nameof(YouTubeTitleSettings.DoubleHealer)] = 0.0373,
        [nameof(YouTubeTitleSettings.DoubleBruiserWithoutTank)] = 0.1492,
        [nameof(YouTubeTitleSettings.NoTank)] = 0.0644,
        [nameof(YouTubeTitleSettings.DoubleTank)] = 0.0746,
        [nameof(YouTubeTitleSettings.TripleBruiser)] = 0,
        [nameof(YouTubeTitleSettings.DoubleSupport)] = 0,
        [nameof(YouTubeTitleSettings.NoRangedAssassin)] = 0.0169,
    };

    /// <summary>
    /// Heroes whose heroes-data2 playstyles (build 98304) disagree with how they are played,
    /// checked against the issue #140 corpus. Each entry gives its reason.
    /// </summary>
    public static Dictionary<string, HeroTagOverride> DefaultHeroTagOverrides() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Brightwing"] = new HeroTagOverride
            {
                Remove = new[] { TeamComposition.SoloLaner },
                Reason =
                    "Healer tagged SoloLaner. She was her team's only healer in 76 of 78 corpus teams, so she is not a soaker.",
            },
            ["Cho"] = new HeroTagOverride
            {
                Remove = new[] { TeamComposition.SoloLaner },
                Reason = "Cho and Gall share one body, so Cho cannot hold a lane apart from Gall.",
            },
            ["Hogger"] = new HeroTagOverride
            {
                Add = new[]
                {
                    TeamComposition.Ganker,
                    TeamComposition.SoloLaner,
                    TeamComposition.WaveClearer,
                },
                Reason =
                    "No playstyles in the catalog. Tagged like Sonya, the bruiser offlaner he is picked as.",
            },
            ["LostVikings"] = new HeroTagOverride
            {
                Add = new[]
                {
                    TeamComposition.RoleSpecialist,
                    TeamComposition.SoloLaner,
                    TeamComposition.WaveClearer,
                },
                Reason = "No playstyles in the catalog. The three Vikings soak lanes apart.",
            },
            ["Maiev"] = new HeroTagOverride
            {
                Add = new[] { TeamComposition.Escaper, TeamComposition.Ganker },
                Reason =
                    "No playstyles in the catalog. Tagged like Illidan and Zeratul, the other melee assassins with a dash.",
            },
            ["Abathur"] = new HeroTagOverride
            {
                IsMelee = false,
                Reason =
                    "isMelee from his 1-range attack, but he plays from behind the wall. He was in 13 of the 31 corpus teams with four melee heroes.",
            },
        };

    /// <summary>The corpus share of games for a key, or null when the table does not have it.</summary>
    public double? Frequency(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Frequencies == null)
        {
            return null;
        }

        return Frequencies.TryGetValue(key, out double value) ? value : null;
    }

    /// <summary>True when the label is rare enough to name. A key with no frequency counts as rare.</summary>
    public bool IsUnusual(string key)
    {
        double? frequency = Frequency(key);
        return frequency == null || frequency.Value < MaxFrequency;
    }
}

/// <summary>
/// One composition rule. Every rule reads <see cref="Enabled"/> and <see cref="Label"/>. The
/// counting rules read <see cref="MinHeroes"/>; Dive also reads <see cref="MinEscapers"/>, and
/// Glass cannon reads only the two rating averages.
/// </summary>
public class CompositionRule
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; }
    public int MinHeroes { get; set; }
    public int MinEscapers { get; set; }
    public double MaxSurvivability { get; set; }
    public double MinDamage { get; set; }
}

/// <summary>Playstyle tags to add to and remove from one hero before the rules count them.</summary>
public class HeroTagOverride
{
    public string[] Add { get; set; } = Array.Empty<string>();
    public string[] Remove { get; set; } = Array.Empty<string>();

    /// <summary>Replaces the catalog's <c>isMelee</c> when set.</summary>
    public bool? IsMelee { get; set; }

    /// <summary>Why the tags changed. Not read by the rules.</summary>
    public string Reason { get; set; }
}
