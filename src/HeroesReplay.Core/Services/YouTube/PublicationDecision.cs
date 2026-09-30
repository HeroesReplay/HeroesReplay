namespace HeroesReplay.Core.Services.YouTube;

public sealed class PublicationDecision
{
    public bool Allow { get; init; }
    public string Reason { get; init; }
    public int Penalty { get; init; }

    public static PublicationDecision Refused(string reason) =>
        new()
        {
            Allow = false,
            Reason = reason,
            Penalty = 0,
        };

    public static PublicationDecision Granted(string reason) =>
        new()
        {
            Allow = true,
            Reason = reason,
            Penalty = 0,
        };
}
