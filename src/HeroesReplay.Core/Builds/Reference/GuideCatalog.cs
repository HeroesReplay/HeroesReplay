using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Reference;

/// <summary>
/// The talent-build reference in memory. <see cref="Embedded"/> reads the one shipped in the
/// assembly (<c>Builds/Reference/icy-veins-builds.json</c>). A hero is found by its unit id, which
/// is the same in every game language, then by its English name.
/// </summary>
public sealed class GuideCatalog : IGuideCatalog
{
    public const string ResourceName = "HeroesReplay.Core.Builds.Reference.icy-veins-builds.json";

    private readonly Dictionary<string, HeroGuide> byUnit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HeroGuide> byName = new(StringComparer.OrdinalIgnoreCase);

    public GuideCatalog(GuideReference reference)
    {
        Reference = reference ?? new GuideReference();
        foreach (HeroGuide guide in Reference.Heroes ?? Array.Empty<HeroGuide>())
        {
            if (guide == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(guide.UnitId))
            {
                byUnit.TryAdd(guide.UnitId.Trim(), guide);
            }

            if (!string.IsNullOrWhiteSpace(guide.Hero))
            {
                byName.TryAdd(guide.Hero.Trim(), guide);
            }
        }
    }

    public GuideReference Reference { get; }

    public static GuideCatalog Embedded()
    {
        using Stream stream =
            typeof(GuideCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded.");
        using var reader = new StreamReader(stream);
        return new GuideCatalog(GuideReferenceJson.Parse(reader.ReadToEnd()));
    }

    public HeroGuide Find(Hero hero)
    {
        if (hero == null)
        {
            return null;
        }

        if (
            !string.IsNullOrWhiteSpace(hero.UnitId)
            && byUnit.TryGetValue(hero.UnitId, out HeroGuide guide)
        )
        {
            return guide;
        }

        return !string.IsNullOrWhiteSpace(hero.Name) && byName.TryGetValue(hero.Name, out guide)
            ? guide
            : null;
    }
}
