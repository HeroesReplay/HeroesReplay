namespace HeroesReplay.Core.Configuration;

/// <summary>
/// Which full-match title forms are used, and which unusual drafts are named.
/// A hero is featured when its name is listed or its release date is inside
/// <see cref="RecentHeroDays"/> of the match.
/// </summary>
public class YouTubeTitleSettings
{
    public static readonly string[] DefaultRecentHeroes = new[] { "Xal'atath" };

    public bool DraftNotes { get; set; } = true;
    public bool NamedPlayerTitles { get; set; } = true;
    public bool RequestedByTitles { get; set; } = true;
    public bool FeatureNewHeroes { get; set; } = true;
    public string FeaturePrefix { get; set; } = "Ft.";

    /// <summary>
    /// Days after release that a hero is still featured. Zero or less uses the name list only.
    /// </summary>
    public int RecentHeroDays { get; set; } = 60;

    /// <summary>
    /// Names to feature even when the catalog has no release date.
    /// Null means <see cref="DefaultRecentHeroes"/>. An empty list uses the date window only.
    /// </summary>
    public string[] RecentHeroes { get; set; }

    public string[] RecentHeroList()
    {
        return RecentHeroes ?? DefaultRecentHeroes;
    }

    public bool NoTankOrHealer { get; set; } = true;
    public bool NoHealer { get; set; } = true;
    public bool DoubleHealer { get; set; } = true;
    public bool TripleHealer { get; set; } = true;
    public bool DoubleBruiserWithoutTank { get; set; } = true;
    public bool NoTank { get; set; } = true;
    public bool DoubleTank { get; set; } = true;
    public bool TripleBruiser { get; set; } = true;
    public bool DoubleSupport { get; set; } = true;
    public bool NoRangedAssassin { get; set; } = true;

    /// <summary>Current hero-select role labels. These match the catalog, not the retired Warrior and Specialist names.</summary>
    public string Tank { get; set; } = "Tank";
    public string Bruiser { get; set; } = "Bruiser";
    public string Healer { get; set; } = "Healer";
    public string Support { get; set; } = "Support";
    public string MeleeAssassin { get; set; } = "Melee Assassin";
    public string RangedAssassin { get; set; } = "Ranged Assassin";
}
