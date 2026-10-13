using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// The match highlight that leads a full match title, and the insight lines in the description
/// (#412), bound at <c>YouTube:Titles:Highlights</c>. Off unless <see cref="Enabled"/>. A highlight
/// names heroes, builds and match shapes, never which team won.
/// </summary>
public class HighlightSettings
{
    public static readonly string[] DefaultOrder =
    {
        nameof(HighlightKind.Pentakill),
        nameof(HighlightKind.CoreRace),
        nameof(HighlightKind.Comeback),
        nameof(HighlightKind.DeathlessCarry),
        nameof(HighlightKind.KillLeader),
        nameof(HighlightKind.SoloLane),
        nameof(HighlightKind.Mvp),
        nameof(HighlightKind.MostFollowed),
    };

    public static readonly string[] DefaultGenericBuildNames =
    {
        "Standard",
        "Basic Attack",
        "Basic Attacks",
        "Standard Auto Attack",
        "Hybrid",
        "Universal",
        "Advanced",
        "Attack",
        "ARAM",
    };

    public bool Enabled { get; set; }

    /// <summary>
    /// The kinds a title may lead with, rarest first: the first kind the match has wins. A kind
    /// left out is never a title. A name that is no kind (one removed since) is ignored, so an older
    /// config still starts. Null means <see cref="DefaultOrder"/>.
    /// </summary>
    public string[] Order { get; set; }

    /// <summary>A build is named in a title only at this score or more, and never when ambiguous.</summary>
    public double TitleBuildMinScore { get; set; } = 0.85;

    /// <summary>A player's build is listed in the description at this score or more.</summary>
    public double DescriptionBuildMinScore { get; set; } = 0.70;

    /// <summary>
    /// Build names too plain for a title once " Build" is dropped ("Standard", "Basic Attacks").
    /// Null means <see cref="DefaultGenericBuildNames"/>.
    /// </summary>
    public string[] GenericBuildNames { get; set; }

    /// <summary>The <c>Solo lanes:</c> line (High confidence only).</summary>
    public bool SoloLaneLine { get; set; } = true;

    /// <summary>The <c>Camps:</c> line.</summary>
    public bool CampLine { get; set; } = true;

    /// <summary>The talent-build block, one line per player with an Icy Veins guide link.</summary>
    public bool TalentBuildLines { get; set; } = true;

    public IReadOnlyList<HighlightKind> Kinds()
    {
        var kinds = new List<HighlightKind>();
        foreach (string name in Order ?? DefaultOrder)
        {
            if (
                Enum.TryParse(name, ignoreCase: true, out HighlightKind kind)
                && !kinds.Contains(kind)
            )
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }

    public bool IsGenericBuildName(string name)
    {
        foreach (string generic in GenericBuildNames ?? DefaultGenericBuildNames)
        {
            if (string.Equals(generic, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
