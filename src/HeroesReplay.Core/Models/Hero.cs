using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models;

public class Hero
{
    public string Name { get; }
    public string UnitId { get; }
    public string HyperlinkId { get; }

    /// <summary>
    /// Used for the TeamBans
    /// </summary>
    public string AttributeId { get; }

    /// <summary>herodata descriptors such as RoleTank, SoloLaner, and AllyHealer.</summary>
    public IReadOnlyList<string> Descriptors { get; }

    /// <summary>Current hero-select role: Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer, or Support.</summary>
    public string Role { get; }

    /// <summary>herodata releaseDate as a UTC calendar day. Null when the catalog has no date.</summary>
    public DateTime? ReleaseDate { get; }

    public Hero(
        string name,
        string unitId,
        string hyperLinkId,
        string attributeId,
        IReadOnlyList<string> descriptors = null,
        string role = null,
        DateTime? releaseDate = null
    )
    {
        Name = name;
        UnitId = unitId;
        HyperlinkId = hyperLinkId;
        AttributeId = attributeId;
        Descriptors = descriptors ?? Array.Empty<string>();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        ReleaseDate = releaseDate;
    }
}
