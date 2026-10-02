using System.Collections.Generic;

namespace HeroesReplay.Core.HeroesProfile;

public sealed class ReplayListing
{
    public static ReplayListing Empty { get; } = new(null, false, 0, null);

    public ReplayListing(
        IReadOnlyList<HeroesProfileReplay> playable,
        bool hadRows,
        int highestId,
        int? nextAfter
    )
    {
        Playable = playable ?? System.Array.Empty<HeroesProfileReplay>();
        HadRows = hadRows;
        HighestId = highestId;
        NextAfter = nextAfter;
    }

    public IReadOnlyList<HeroesProfileReplay> Playable { get; }

    public bool HadRows { get; }

    public int HighestId { get; }

    public int? NextAfter { get; }
}
