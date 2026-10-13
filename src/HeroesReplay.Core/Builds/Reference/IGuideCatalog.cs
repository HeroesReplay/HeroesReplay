using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Reference;

/// <summary>The talent-build reference, looked up by hero.</summary>
public interface IGuideCatalog
{
    GuideReference Reference { get; }

    /// <summary>The hero's guide, or null when the reference has none.</summary>
    HeroGuide Find(Hero hero);
}
