using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared;

public class Hero
{
    public string Name { get; }
    public string UnitId { get; }
    public string HyperlinkId { get; }

    /// <summary>
    /// Used for the TeamBans
    /// </summary>
    public string AttributeId { get; }

    /// <summary>herodata playstyles such as Ganker, TowerPusher, and PowerfulLaner.</summary>
    public IReadOnlyList<string> Descriptors { get; }

    /// <summary>Current hero-select role: Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer, or Support.</summary>
    public string Role { get; }

    /// <summary>herodata releaseDate as a UTC calendar day. Null when the catalog has no date.</summary>
    public DateTime? ReleaseDate { get; }

    /// <summary>herodata isMelee. Null when the catalog did not say.</summary>
    public bool? IsMelee { get; }

    /// <summary>herodata ratings (1 to 10). Null when the catalog has none.</summary>
    public HeroRatings Ratings { get; }

    public Hero(
        string name,
        string unitId,
        string hyperLinkId,
        string attributeId,
        IReadOnlyList<string> descriptors = null,
        string role = null,
        DateTime? releaseDate = null,
        bool? isMelee = null,
        HeroRatings ratings = null
    )
    {
        Name = name;
        UnitId = unitId;
        HyperlinkId = hyperLinkId;
        AttributeId = attributeId;
        Descriptors = descriptors ?? Array.Empty<string>();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        ReleaseDate = releaseDate;
        IsMelee = isMelee;
        Ratings = ratings;
    }
}

/// <summary>The herodata hero-select ratings, each from 1 to 10.</summary>
public sealed record HeroRatings(
    double Damage,
    double Survivability,
    double Utility,
    double Complexity
);
