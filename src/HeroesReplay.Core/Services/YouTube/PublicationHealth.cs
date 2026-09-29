namespace HeroesReplay.Core.Services.YouTube;

public sealed class PublicationHealthReport
{
    public int Pending { get; init; }
    public int Uploaded { get; init; }
    public int Deferred { get; init; }
    public string Limit { get; init; }
    public string PolicyVersion { get; init; }
}

/// <summary>
/// Read-only counts from the ledger the caller already has. It does not reserve a slot
/// or call YouTube.
/// </summary>
public static class PublicationHealth
{
    public static PublicationHealthReport Summarize(
        int pending,
        int uploaded,
        int deferred,
        bool quotaExhausted,
        string policyVersion
    )
    {
        if (pending < 0)
        {
            pending = 0;
        }

        if (uploaded < 0)
        {
            uploaded = 0;
        }

        if (deferred < 0)
        {
            deferred = 0;
        }

        string limit = "none";
        if (quotaExhausted)
        {
            limit = "quota";
        }
        else if (deferred > 0)
        {
            limit = "publication";
        }

        return new PublicationHealthReport
        {
            Pending = pending,
            Uploaded = uploaded,
            Deferred = deferred,
            Limit = limit,
            PolicyVersion = string.IsNullOrWhiteSpace(policyVersion) ? "1" : policyVersion.Trim(),
        };
    }
}
