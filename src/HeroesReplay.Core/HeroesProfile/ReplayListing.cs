using System.Collections.Generic;

namespace HeroesReplay.Core.HeroesProfile;

public sealed class ReplayListing
{
    /// <summary>Heroes Profile answered the list with no rows.</summary>
    public static ReplayListing Empty { get; } = new(null, false, 0, null);

    /// <summary>
    /// Heroes Profile did not answer the list: no answer, a timeout, or a 429 or 5xx after the
    /// HTTP pipeline's retries. Transient: the caller lists again as before.
    /// </summary>
    public static ReplayListing Unanswered { get; } =
        new(null, false, 0, null, listed: false, rejectedStatus: null);

    public ReplayListing(
        IReadOnlyList<HeroesProfileReplay> playable,
        bool hadRows,
        int highestId,
        int? nextAfter
    )
        : this(playable, hadRows, highestId, nextAfter, listed: true, rejectedStatus: null) { }

    private ReplayListing(
        IReadOnlyList<HeroesProfileReplay> playable,
        bool hadRows,
        int highestId,
        int? nextAfter,
        bool listed,
        int? rejectedStatus
    )
    {
        Playable = playable ?? System.Array.Empty<HeroesProfileReplay>();
        HadRows = hadRows;
        HighestId = highestId;
        NextAfter = nextAfter;
        Listed = listed;
        RejectedStatus = rejectedStatus;
    }

    /// <summary>
    /// Heroes Profile refused the API key (HTTP 401 or 403). Not transient: the same key gets the
    /// same answer until someone fixes it (#358).
    /// </summary>
    public static ReplayListing Rejected(int status) =>
        new(null, false, 0, null, listed: false, rejectedStatus: status);

    public IReadOnlyList<HeroesProfileReplay> Playable { get; }

    public bool HadRows { get; }

    public int HighestId { get; }

    public int? NextAfter { get; }

    /// <summary>True when Heroes Profile answered the list, with rows or without.</summary>
    public bool Listed { get; }

    /// <summary>The HTTP status when Heroes Profile refused the key (401 or 403), else null.</summary>
    public int? RejectedStatus { get; }
}
